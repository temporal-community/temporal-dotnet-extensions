#pragma warning disable CA2007 // Workflow continuations must retain their scheduler.
using System.Collections.Concurrent;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects.Examples;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Objects;

[Workflow]
public sealed class RollingSignalInbox : SignalInbox
{
    private bool _rollover;

    [WorkflowInit]
    public RollingSignalInbox(DurableObjectSnapshot<InboxState>? snapshot = null) : base(snapshot) { }

    [WorkflowRun]
    public override Task RunAsync(DurableObjectSnapshot<InboxState>? snapshot = null) => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task RequestRolloverAsync()
    {
        _rollover = true;
        return Task.CompletedTask;
    }

    protected override bool ShouldContinueAsNew() => _rollover;
}

// A bounded failing Activity stand-in, not a demonstration of external transactional dedup.
public sealed class RetryInboxActivities(Slice3Barrier firstAttempt) : ISignalInboxActivities
{
    private int _attempts;
    public int Attempts => Volatile.Read(ref _attempts);
    public ConcurrentQueue<string> SuccessfulIds { get; } = new();

    [Activity("ProcessInboxEvent")]
    public async Task ProcessAsync(InboxEvent item)
    {
        if (Interlocked.Increment(ref _attempts) == 1)
        {
            firstAttempt.Reached.TrySetResult();
            await firstAttempt.Release.Task.WaitAsync(ActivityExecutionContext.Current.CancellationToken);
            throw new ApplicationFailureException("Retry on a later timer tick.", nonRetryable: true);
        }

        SuccessfulIds.Enqueue(item.EventId);
    }
}

public sealed class SignalEffectActivities
{
    private int _effects;
    public int Effects => Volatile.Read(ref _effects);

    [Activity]
    public Task ApplyAsync()
    {
        Interlocked.Increment(ref _effects);
        return Task.CompletedTask;
    }
}
