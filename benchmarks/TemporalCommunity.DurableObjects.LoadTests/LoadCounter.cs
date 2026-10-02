using Temporalio.Activities;
using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects.LoadTests;

public sealed record LoadCounterState(long CompletedUpdates, int Activations, int HistoryThreshold, string Payload);

public sealed record CounterObservation(
    long CompletedUpdates, int Activations, int InFlightHandlers,
    string RunId, int HistoryLength, long HistoryBytes, int HistoryThreshold);

[Workflow]
public interface ILoadCounter : IDurableObject
{
    [WorkflowUpdate] Task ConfigureAsync(int historyThreshold);
    [WorkflowUpdate] Task AddAsync(string payload, int activityDelayMs);
    [WorkflowQuery] CounterObservation ReadObservation();
}

[Workflow]
public sealed class LoadCounter : DurableObjectBase<LoadCounterState>, ILoadCounter
{
    private int _inFlight;

    [WorkflowInit]
    public LoadCounter(DurableObjectSnapshot<LoadCounterState>? snapshot = null)
        : base(snapshot, new LoadCounterState(0, 0, 10_000, string.Empty)) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<LoadCounterState>? snapshot = null) => DurableObjectRunAsync();

    protected override Task OnActivateAsync()
    {
        State = State with { Activations = State.Activations + 1 };
        return Task.CompletedTask;
    }

    protected override bool ShouldContinueAsNew() =>
        base.ShouldContinueAsNew() || Workflow.CurrentHistoryLength >= State.HistoryThreshold;

    [WorkflowUpdate]
    public Task ConfigureAsync(int historyThreshold)
    {
        State = State with { HistoryThreshold = historyThreshold };
        return Task.CompletedTask;
    }

    [WorkflowUpdate]
    public async Task AddAsync(string payload, int activityDelayMs)
    {
        _inFlight++;
        try
        {
            if (activityDelayMs > 0)
            {
                await ExecuteActivityAsync(
                    (LoadActivities activity) => activity.WaitAsync(activityDelayMs),
                    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
            }

            // Read the current state after awaiting, so the same workload remains correct when
            // Serialize=false permits overlapping handlers. No stale read is carried over await.
            State = State with { CompletedUpdates = State.CompletedUpdates + 1, Payload = payload };
            RecordActivity();
        }
        finally
        {
            _inFlight--;
        }
    }

    [WorkflowQuery]
    public CounterObservation ReadObservation() => new(
        State.CompletedUpdates, State.Activations, _inFlight, Workflow.Info.RunId,
        Workflow.CurrentHistoryLength, Workflow.CurrentHistorySize, State.HistoryThreshold);
}

public sealed class LoadActivities
{
    [Activity]
    public Task WaitAsync(int milliseconds) =>
        Task.Delay(milliseconds, ActivityExecutionContext.Current.CancellationToken);
}
