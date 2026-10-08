#pragma warning disable CA1822 // Workflow methods must be instance methods.
#pragma warning disable CA2007 // Workflow code must stay on the workflow scheduler.
using Temporalio.Activities;
using Temporalio.Common;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Objects;

public readonly record struct SnapshotAdmissionState(int Count);

public sealed record ReminderRetryState(
    int DeliveryAttempts,
    int BusinessEffects,
    string DeliveryId,
    string FirstRunId);

public sealed class Slice3Barrier
{
    public TaskCompletionSource Reached { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class Slice3BarrierActivities(Slice3Barrier barrier)
{
    [Activity]
    public async Task WaitAsync()
    {
        barrier.Reached.TrySetResult();
        await barrier.Release.Task.WaitAsync(ActivityExecutionContext.Current.CancellationToken);
    }
}

public sealed class Slice3CanBarriers
{
    public TaskCompletionSource EnteredUpdateReached { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ReleaseEnteredUpdate { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource SnapshotReached { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ReleaseSnapshot { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class Slice3CanBarrierActivities(Slice3CanBarriers barriers)
{
    [Activity]
    public async Task WaitForEnteredUpdateReleaseAsync()
    {
        barriers.EnteredUpdateReached.TrySetResult();
        await barriers.ReleaseEnteredUpdate.Task.WaitAsync(
            ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity]
    public async Task WaitForSnapshotReleaseAsync()
    {
        barriers.SnapshotReached.TrySetResult();
        await barriers.ReleaseSnapshot.Task.WaitAsync(
            ActivityExecutionContext.Current.CancellationToken);
    }
}

[Workflow]
public sealed class BlockedActivationObject : DurableObjectBase
{
    private string _phase = "constructed";

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    protected override async Task OnActivateAsync()
    {
        _phase = "activating";
        await Workflow.ExecuteActivityAsync(
            (Slice3BarrierActivities activities) => activities.WaitAsync(),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromSeconds(30),
                RetryPolicy = new RetryPolicy { MaximumAttempts = 1 },
            });
        _phase = "active";
    }

    [WorkflowUpdate]
    public Task<string> ObserveUpdateAsync() => Task.FromResult($"update:{_phase}");

    [WorkflowQuery]
    public string ReadPhase() => _phase;
}

[Workflow]
public sealed class SnapshotAdmissionObject : DurableObjectBase<SnapshotAdmissionState>
{
    private bool _continueAsNew;
    private string _phase = "active";

    [WorkflowInit]
    public SnapshotAdmissionObject(DurableObjectSnapshot<SnapshotAdmissionState>? snapshot = null)
        : base(snapshot, new SnapshotAdmissionState(0)) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<SnapshotAdmissionState>? snapshot = null) =>
        DurableObjectRunAsync();

    protected override bool ShouldContinueAsNew() => _continueAsNew;

    protected override async Task<SnapshotAdmissionState> PrepareStateForContinueAsNewAsync(
        SnapshotAdmissionState state)
    {
        _phase = "snapshot-preparing";
        await Workflow.ExecuteActivityAsync(
            (Slice3CanBarrierActivities activities) =>
                activities.WaitForSnapshotReleaseAsync(),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromSeconds(30),
                RetryPolicy = new RetryPolicy { MaximumAttempts = 1 },
            });
        return State;
    }

    [WorkflowUpdate]
    public async Task<int> EnterAndRequestContinueAsNewAsync(int amount)
    {
        State = State with { Count = State.Count + amount };
        _continueAsNew = true;
        await Workflow.ExecuteActivityAsync(
            (Slice3CanBarrierActivities activities) =>
                activities.WaitForEnteredUpdateReleaseAsync(),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromSeconds(30),
                RetryPolicy = new RetryPolicy { MaximumAttempts = 1 },
            });
        return State.Count;
    }

    [WorkflowUpdate]
    public Task<int> AddDuringSnapshotAsync(int amount)
    {
        State = State with { Count = State.Count + amount };
        return Task.FromResult(State.Count);
    }

    [WorkflowQuery]
    public string ReadPhase() => _phase;

    [WorkflowQuery]
    public int ReadCount() => State.Count;
}

[Workflow]
public sealed class ReminderRetryObject : DurableObjectBase<ReminderRetryState>, IReminderReceiver
{
    private bool _continueAsNew;

    [WorkflowInit]
    public ReminderRetryObject(DurableObjectSnapshot<ReminderRetryState>? snapshot = null)
        : base(snapshot, new ReminderRetryState(0, 0, string.Empty, string.Empty)) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<ReminderRetryState>? snapshot = null) =>
        DurableObjectRunAsync();

    protected override bool ShouldContinueAsNew() => _continueAsNew;

    [WorkflowUpdate]
    public Task OnReminderAsync(string reminderName, ReminderDeliveryContext context)
    {
        var firstDelivery = !StringComparer.Ordinal.Equals(State.DeliveryId, context.DeliveryId);
        State = State with
        {
            DeliveryAttempts = State.DeliveryAttempts + 1,
            BusinessEffects = State.BusinessEffects + (firstDelivery ? 1 : 0),
            DeliveryId = context.DeliveryId,
            FirstRunId = State.FirstRunId.Length == 0 ? Workflow.Info.RunId : State.FirstRunId,
        };
        _continueAsNew = State.DeliveryAttempts == 1;
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public ReminderRetryState ReadState() => State;
}

[Workflow]
public sealed class LifecycleTerminalStatusObject : DurableObjectBase
{
    private bool _prepareSnapshot;
    private string _phase = "active";

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    protected override async Task OnActivateAsync()
    {
        if (Workflow.Info.WorkflowId.Contains("-activation-", StringComparison.Ordinal))
        {
            _phase = "activation-blocked";
            await Workflow.DelayAsync(Timeout.InfiniteTimeSpan);
        }
    }

    protected override bool ShouldContinueAsNew() => _prepareSnapshot;

    protected override async Task<IReadOnlyCollection<object?>> OnBeforeContinueAsNewAsync()
    {
        _phase = "snapshot-blocked";
        await Workflow.DelayAsync(Timeout.InfiniteTimeSpan);
        return Array.Empty<object?>();
    }

    protected override async Task OnDeactivateAsync()
    {
        if (Workflow.Info.WorkflowId.Contains("-deactivation-", StringComparison.Ordinal))
        {
            _phase = "deactivation-blocked";
            await Workflow.DelayAsync(Timeout.InfiniteTimeSpan);
        }
    }

    [WorkflowUpdate]
    public Task PrepareSnapshotAsync()
    {
        _prepareSnapshot = true;
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public string ReadPhase() => _phase;
}

[Workflow]
public sealed class AuthorizationCallbackObject : DurableObjectBase
{
    private int _count;

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task ThrowAuthorizationAsync() =>
        throw new InvalidOperationException("User handler must not run.");

    [WorkflowUpdate]
    public Task<int> IncrementAsync()
    {
        _count++;
        return Task.FromResult(_count);
    }
}
