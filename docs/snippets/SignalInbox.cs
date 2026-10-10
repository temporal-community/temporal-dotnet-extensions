// Compiled and exercised by the DurableObjects integration project.
#pragma warning disable CA1822 // Workflow and activity contracts require instance methods.
#pragma warning disable CA2007 // Workflow awaits must retain the workflow scheduler.
#pragma warning disable CA1031 // Processing failures must be contained inside the timer.
#pragma warning disable CA1515 // Public workflow contracts enable generated clients.
using Temporalio.Activities;
using Temporalio.Common;
using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects.Examples;

public sealed record InboxEvent(string EventId, string Payload);
public sealed record InboxState(
    IReadOnlyList<InboxEvent> Pending,
    IReadOnlyList<string> RecentlyCompleted,
    int FailedAttempts);

[Workflow]
public interface ISignalInbox : IDurableObject
{
    [WorkflowSignal] Task EnqueueAsync(InboxEvent item);
    [WorkflowQuery] InboxState ReadState();
}

public interface ISignalInboxActivities
{
    // The implementation must atomically deduplicate EventId with its external effect,
    // or use an idempotent external operation. List removal is not that transaction.
    [Activity("ProcessInboxEvent")] Task ProcessAsync(InboxEvent item);
}

[Workflow]
public class SignalInbox : DurableObjectBase<InboxState>, ISignalInbox
{
    private const int MaxPending = 256;
    private const int BatchSize = 16;
    private const int CompletedIdRetention = 1_024;

    private static readonly ActivityOptions ProcessOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(10),
        ScheduleToCloseTimeout = TimeSpan.FromSeconds(20),
        RetryPolicy = new RetryPolicy { MaximumAttempts = 1 },
    };

    [WorkflowInit]
    public SignalInbox(DurableObjectSnapshot<InboxState>? snapshot = null)
        : base(snapshot, new InboxState([], [], 0)) { }

    [WorkflowRun]
    public virtual Task RunAsync(DurableObjectSnapshot<InboxState>? snapshot = null) => DurableObjectRunAsync();

    protected override Task OnActivateAsync()
    {
        ScheduleTimer("drain", TimeSpan.FromSeconds(5), recurring: true);
        return Task.CompletedTask;
    }

    [WorkflowSignal]
    public Task EnqueueAsync(InboxEvent item)
    {
        // Validate before mutation. Signal failures cannot be returned to the sender.
        if (item is null || string.IsNullOrWhiteSpace(item.EventId) || item.EventId.Length > 128 ||
            item.Payload is null || item.Payload.Length > 4_096)
            throw new ArgumentException("Invalid inbox event.", nameof(item));

        if (State.Pending.Any(p => p.EventId == item.EventId) ||
            State.RecentlyCompleted.Contains(item.EventId))
            return Task.CompletedTask;

        if (State.Pending.Count >= MaxPending)
            throw new InvalidOperationException("Inbox capacity exceeded.");

        State = State with { Pending = [.. State.Pending, item] };
        return Task.CompletedTask;
    }

    [WorkflowQuery] public InboxState ReadState() => State;

    protected override async Task OnTimerAsync(string name)
    {
        if (name != "drain" || Workflow.CancellationToken.IsCancellationRequested) return;

        // One timer is the sole consumer. Enqueue only appends synchronously; after each
        // await removal uses current State, not a stale snapshot that could lose an append.
        foreach (var item in State.Pending.Take(BatchSize).ToList())
        {
            try
            {
                await ExecuteActivityAsync(
                    (ISignalInboxActivities activities) => activities.ProcessAsync(item),
                    ProcessOptions);
            }
            catch (Exception ex) when (ex is not ContinueAsNewException)
            {
                // Keep the item after any processing failure, including cancellation.
                // An escaped timer failure would terminate the object.
                State = State with { FailedAttempts = State.FailedAttempts + 1 };
                return; // Retry on a later tick; a poison item blocks this ordered queue.
            }

            State = State with
            {
                Pending = [.. State.Pending.Where(p => p.EventId != item.EventId)],
                RecentlyCompleted = [.. State.RecentlyCompleted.TakeLast(CompletedIdRetention - 1), item.EventId],
            };
            if (Workflow.CancellationToken.IsCancellationRequested) return;
        }
    }
}
