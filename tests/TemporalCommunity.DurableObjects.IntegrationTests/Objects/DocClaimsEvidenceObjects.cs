#pragma warning disable CA1822 // Workflow methods must be instance methods.
#pragma warning disable CA2007 // Workflow code must stay on the workflow scheduler.
using Temporalio.Common;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Objects;

// Evidence fixtures for documentation claims R2a/R2b (timer vs. update interleaving),
// R3 (OnTimerAsync failure), and R-limit (in-flight Update limit). Barriers reuse the
// activity-backed Slice3Barrier so the update handler is suspended deterministically.
internal static class DocClaimsBarrier
{
    public static Task WaitAsync() =>
        Workflow.ExecuteActivityAsync(
            (Slice3BarrierActivities activities) => activities.WaitAsync(),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromSeconds(60),
                RetryPolicy = new RetryPolicy { MaximumAttempts = 1 },
            });
}

/// <summary>
/// The update reads State, schedules a timer (due in 1s), suspends at an activity barrier, then
/// writes read+1. OnTimerAsync adds 100. Mode controls which bodies use RunSerializedAsync:
/// "none" (neither), "timer" (only OnTimerAsync), "both" (update body and OnTimerAsync).
/// </summary>
[Workflow]
public sealed class TimerInterleavingObject : DurableObjectBase<int>
{
    private string _mode = "none";
    private string _timerPhase = "not-fired";

    [WorkflowInit]
    public TimerInterleavingObject(DurableObjectSnapshot<int>? snapshot = null)
        : base(snapshot, 0) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<int>? snapshot = null) => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task<int> ReadModifyWriteAsync(string mode)
    {
        _mode = mode;
        return mode == "both" ? RunSerializedAsync(BodyAsync) : BodyAsync();

        async Task<int> BodyAsync()
        {
            var read = State;
            ScheduleTimer("bump", TimeSpan.FromSeconds(1));
            await DocClaimsBarrier.WaitAsync();
            State = read + 1;
            return State;
        }
    }

    protected override Task OnTimerAsync(string name)
    {
        _timerPhase = "entered";
        if (_mode is "timer" or "both")
        {
            return RunSerializedAsync(() =>
            {
                Bump();
                return Task.CompletedTask;
            });
        }

        Bump();
        return Task.CompletedTask;
    }

    private void Bump()
    {
        State += 100;
        _timerPhase = "fired";
    }

    [WorkflowQuery]
    public string ReadTimerPhase() => _timerPhase;

    [WorkflowQuery]
    public int ReadCount() => State;
}

public sealed class DocClaimsTwoPhaseBarrier
{
    public TaskCompletionSource FirstReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class DocClaimsTwoPhaseBarrierActivities(DocClaimsTwoPhaseBarrier barrier)
{
    [Temporalio.Activities.Activity]
    public async Task FirstPhaseAsync()
    {
        barrier.FirstReached.TrySetResult();
        await barrier.ReleaseFirst.Task.WaitAsync(
            Temporalio.Activities.ActivityExecutionContext.Current.CancellationToken);
    }

    [Temporalio.Activities.Activity]
    public async Task SecondPhaseAsync()
    {
        barrier.SecondReached.TrySetResult();
        await barrier.ReleaseSecond.Task.WaitAsync(
            Temporalio.Activities.ActivityExecutionContext.Current.CancellationToken);
    }
}

[Workflow]
public interface IFailingTimerObject : IDurableObject
{
    [WorkflowUpdate]
    Task<int> AddAsync(int amount);

    [WorkflowUpdate]
    Task<int> AddThenArmFailingTimerAndBlockAsync(int amount);

    [WorkflowQuery]
    int ReadCount();
}

/// <summary>
/// AddThenArmFailingTimerAndBlockAsync mutates State, suspends at barrier phase 1 (so the test
/// can queue another update behind the interceptor gate), then schedules an immediately-due
/// timer whose OnTimerAsync throws a plain InvalidOperationException and suspends at phase 2.
/// </summary>
[Workflow]
public sealed class FailingTimerObject : DurableObjectBase<int>, IFailingTimerObject
{
    [WorkflowInit]
    public FailingTimerObject(DurableObjectSnapshot<int>? snapshot = null)
        : base(snapshot, 0) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<int>? snapshot = null) => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task<int> AddAsync(int amount)
    {
        State += amount;
        return Task.FromResult(State);
    }

    [WorkflowUpdate]
    public async Task<int> AddThenArmFailingTimerAndBlockAsync(int amount)
    {
        State += amount;
        var options = new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromSeconds(60),
            RetryPolicy = new RetryPolicy { MaximumAttempts = 1 },
        };
        await Workflow.ExecuteActivityAsync(
            (DocClaimsTwoPhaseBarrierActivities a) => a.FirstPhaseAsync(), options);
        ScheduleTimer("boom", TimeSpan.Zero);
        await Workflow.ExecuteActivityAsync(
            (DocClaimsTwoPhaseBarrierActivities a) => a.SecondPhaseAsync(), options);
        return State;
    }

    protected override Task OnTimerAsync(string name) =>
        throw new InvalidOperationException($"plain failure from timer '{name}'");

    [WorkflowQuery]
    public int ReadCount() => State;
}

[Workflow]
public interface IInFlightLimitObject : IDurableObject
{
    [WorkflowUpdate]
    Task<int> HoldAsync();

    [WorkflowUpdate]
    Task<int> IncrementAsync();

    [WorkflowQuery]
    int ReadCount();
}

[Workflow]
public sealed class InFlightLimitObject : DurableObjectBase<int>, IInFlightLimitObject
{
    [WorkflowInit]
    public InFlightLimitObject(DurableObjectSnapshot<int>? snapshot = null)
        : base(snapshot, 0) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<int>? snapshot = null) => DurableObjectRunAsync();

    [WorkflowUpdate]
    public async Task<int> HoldAsync()
    {
        await DocClaimsBarrier.WaitAsync();
        return State;
    }

    [WorkflowUpdate]
    public Task<int> IncrementAsync()
    {
        State++;
        return Task.FromResult(State);
    }

    [WorkflowQuery]
    public int ReadCount() => State;
}
