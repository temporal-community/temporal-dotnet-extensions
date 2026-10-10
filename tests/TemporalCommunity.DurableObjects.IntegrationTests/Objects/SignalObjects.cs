#pragma warning disable CA1822 // SDK workflow handlers must be instance methods.
#pragma warning disable CA2007 // Workflow continuations must stay on the workflow scheduler.
using Temporalio.Common;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Objects;

[Workflow]
public interface IColdSignalObject : IDurableObject
{
    [WorkflowSignal] Task AppendAsync(int value);
    [WorkflowQuery] string ReadPhase();
    [WorkflowQuery] int ReadCount();
}

[Workflow]
public sealed class ColdSignalObject : DurableObjectBase, IColdSignalObject
{
    private string _phase = "constructed";
    private int _count;

    [WorkflowRun] public Task RunAsync() => DurableObjectRunAsync();

    protected override async Task OnActivateAsync()
    {
        _phase = "activating";
        await Workflow.ExecuteActivityAsync(
            (Slice3BarrierActivities activities) => activities.WaitAsync(),
            SignalObject.BarrierOptions);
        _phase = "active";
    }

    [WorkflowSignal]
    public Task AppendAsync(int value)
    {
        if (_phase != "active") throw new InvalidOperationException("Signal ran before activation.");
        _count += value;
        return Task.CompletedTask;
    }

    [WorkflowQuery] public string ReadPhase() => _phase;
    [WorkflowQuery] public int ReadCount() => _count;
}

public sealed record SignalState(int Count, IReadOnlyList<string> Order, int SnapshotPasses);

[Workflow]
public sealed class SignalObject : DurableObjectBase<SignalState>
{
    public static readonly ActivityOptions BarrierOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(30),
        RetryPolicy = new RetryPolicy { MaximumAttempts = 1 },
    };

    private bool _rollover;
    private bool _blockDeactivation;
    private bool _snapshotWait;

    [WorkflowInit]
    public SignalObject(DurableObjectSnapshot<SignalState>? snapshot = null)
        : base(snapshot, new SignalState(0, [], 0)) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<SignalState>? snapshot = null) => DurableObjectRunAsync();

    [WorkflowSignal]
    public Task AppendAsync(int value)
    {
        State = State with { Count = State.Count + value, Order = [.. State.Order, $"signal:{value}"] };
        return Task.CompletedTask;
    }

    [WorkflowUpdate]
    public Task<int> AddAsync(int value)
    {
        State = State with { Count = State.Count + value };
        return Task.FromResult(State.Count);
    }

    [WorkflowUpdate]
    public async Task BlockUpdateAsync(bool rollover)
    {
        State = State with { Order = [.. State.Order, "update-start"] };
        _rollover = rollover;
        await Workflow.ExecuteActivityAsync(
            (Slice3CanBarrierActivities activities) => activities.WaitForEnteredUpdateReleaseAsync(),
            BarrierOptions);
        State = State with { Order = [.. State.Order, "update-end"] };
    }

    [WorkflowSignal]
    public async Task BlockSignalAsync(bool rollover)
    {
        State = State with { Order = [.. State.Order, "signal-start"] };
        _rollover = rollover;
        await Workflow.ExecuteActivityAsync(
            (Slice3CanBarrierActivities activities) => activities.WaitForEnteredUpdateReleaseAsync(),
            BarrierOptions);
        State = State with { Count = State.Count + 10, Order = [.. State.Order, "signal-end"] };
    }

    [WorkflowSignal]
    public Task FailAsync(string kind)
    {
        State = State with { Count = State.Count + 1 };
        return kind switch
        {
            "ordinary" => throw new InvalidOperationException("secret-handler-message"),
            "application" => throw new ApplicationFailureException("secret-application-message"),
            "unrelated-cancel" => throw new OperationCanceledException("secret-cancellation-message"),
            _ => Task.CompletedTask,
        };
    }

    [WorkflowSignal]
    public async Task AwaitCancellationAsync()
    {
        State = State with { Order = [.. State.Order, "awaiting-cancel"] };
        await Workflow.WaitConditionAsync(() => Workflow.CancellationToken.IsCancellationRequested);
        throw new OperationCanceledException(Workflow.CancellationToken);
    }

    [WorkflowSignal]
    public async Task EffectThenFailAsync()
    {
        State = State with { Count = State.Count + 1 };
        await Workflow.ExecuteActivityAsync(
            (SignalEffectActivities activities) => activities.ApplyAsync(), BarrierOptions);
        throw new InvalidOperationException("Failure after Activity completion.");
    }

    [WorkflowSignal]
    public async Task BlockThenDeactivateAsync()
    {
        await Workflow.ExecuteActivityAsync(
            (Slice3CanBarrierActivities activities) => activities.WaitForEnteredUpdateReleaseAsync(),
            BarrierOptions);
        _blockDeactivation = true;
        await DeactivateAsync();
    }

    [WorkflowSignal]
    public Task ContinueDirectlyAsync() =>
        throw Workflow.CreateContinueAsNewException(
            nameof(SignalObject), [new DurableObjectSnapshot<SignalState>(State)]);

    [WorkflowUpdate]
    public Task RequestSnapshotAsync()
    {
        _snapshotWait = true;
        _rollover = true;
        return Task.CompletedTask;
    }

    [WorkflowUpdate]
    public Task BeginDeactivationAsync()
    {
        _blockDeactivation = true;
        return DeactivateAsync();
    }

    [WorkflowSignal]
    public Task BeginSignalDeactivationAsync()
    {
        _blockDeactivation = true;
        return DeactivateAsync();
    }

    [WorkflowSignal]
    public Task BeginSelfDeactivationAsync()
    {
        _blockDeactivation = true;
        Deactivate();
        return Task.CompletedTask;
    }

    [WorkflowQuery] public SignalState ReadState() => State;
    [WorkflowQuery] public string ReadPhase() => IsSnapshotOrDeactivation();

    private string IsSnapshotOrDeactivation() =>
        _blockDeactivation ? "deactivating" : _rollover ? "rolling" : "active";

    protected override bool ShouldContinueAsNew() => _rollover;

    protected override async Task<SignalState> PrepareStateForContinueAsNewAsync(SignalState state)
    {
        State = State with { SnapshotPasses = State.SnapshotPasses + 1 };
        var captured = State; // Deliberately stale if a signal arrives during this await.
        if (_snapshotWait)
        {
            await Workflow.ExecuteActivityAsync(
                (Slice3CanBarrierActivities activities) => activities.WaitForSnapshotReleaseAsync(),
                BarrierOptions);
        }

        return captured;
    }

    protected override async Task OnDeactivateAsync()
    {
        if (_blockDeactivation)
        {
            await Workflow.ExecuteActivityAsync(
                (Slice3CanBarrierActivities activities) => activities.WaitForSnapshotReleaseAsync(),
                BarrierOptions);
        }
    }

}
