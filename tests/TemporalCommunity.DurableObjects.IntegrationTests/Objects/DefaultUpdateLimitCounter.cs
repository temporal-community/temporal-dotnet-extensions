using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Objects;

public sealed record UpdateLimitRollover(string RunId, int Count, int HistoryLength, bool Suggested);

public sealed record UpdateLimitState(int Count, IReadOnlyList<UpdateLimitRollover> Rollovers);

[Workflow]
public interface IDefaultUpdateLimitCounter : IDurableObject
{
    [WorkflowUpdate]
    Task<int> IncrementAsync();

    [WorkflowQuery]
    UpdateLimitState ReadState();
}

// No custom options or ShouldContinueAsNew override: exercise the production defaults.
[Workflow]
public sealed class DefaultUpdateLimitCounter :
    DurableObjectBase<UpdateLimitState>, IDefaultUpdateLimitCounter
{
    [WorkflowInit]
    public DefaultUpdateLimitCounter(DurableObjectSnapshot<UpdateLimitState>? snapshot = null)
        : base(snapshot, new UpdateLimitState(0, [])) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<UpdateLimitState>? snapshot = null) =>
        DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task<int> IncrementAsync()
    {
        State = State with { Count = State.Count + 1 };
        return Task.FromResult(State.Count);
    }

    [WorkflowQuery]
    public UpdateLimitState ReadState() => State;

    protected override Task<UpdateLimitState> PrepareStateForContinueAsNewAsync(UpdateLimitState state) =>
        Task.FromResult(state with
        {
            Rollovers = [.. state.Rollovers, new UpdateLimitRollover(
                Workflow.Info.RunId, state.Count, Workflow.CurrentHistoryLength,
                Workflow.ContinueAsNewSuggested)],
        });
}

// Non-DurableObject control: deliberately never CAN, to measure the server's effective
// Update limit rather than infer it from command-line configuration or early rollover.
[Workflow]
public sealed class UpdateLimitServerControl
{
    private int _count;

    [WorkflowRun]
    public Task RunAsync() => Workflow.WaitConditionAsync(() => false);

    [WorkflowUpdate]
    public Task<int> IncrementAsync() => Task.FromResult(++_count);

    [WorkflowQuery]
    public int ReadCount() => _count;
}
