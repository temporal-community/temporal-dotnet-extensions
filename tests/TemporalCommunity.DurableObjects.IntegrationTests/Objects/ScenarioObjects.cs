#pragma warning disable CA1822 // Mark members as static — workflow methods must be instance methods
#pragma warning disable CA2007 // ConfigureAwait — workflow code must not use ConfigureAwait(false)
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Objects;

// ---------------------------------------------------------------------------
// Shared state record
// ---------------------------------------------------------------------------

public record CounterState(int Count)
{
    public CounterState Increment(int amount) => this with { Count = Count + amount };
}

// ---------------------------------------------------------------------------
// Activities
// ---------------------------------------------------------------------------

public class ScenarioActivities
{
    [Temporalio.Activities.Activity]
    public string Audit(string objectId, int newCount)
    {
        var receipt = $"audit:{objectId}:{newCount}:{Guid.NewGuid():N}";
        return receipt;
    }
}

// ---------------------------------------------------------------------------
// Scenario A, C, D, I, J — AuditedCounter
// ---------------------------------------------------------------------------

[Workflow]
public interface IAuditedCounter : IDurableObject
{
    [WorkflowUpdate] Task<int> IncrementAsync(int amount);
    [WorkflowUpdate] Task<string> IncrementWithAuditAsync(int amount);
    [WorkflowUpdate] Task<int> IncrementSlowlyAsync(int amount);
    [WorkflowUpdate] Task<int> IncrementSlowlySerializedAsync(int amount);
    [WorkflowQuery] int GetCount();
}

[Workflow]
public class AuditedCounter : DurableObjectBase, IAuditedCounter
{
    private CounterState _state;

    [WorkflowInit]
    public AuditedCounter(CounterState? state) => _state = state ?? new CounterState(0);

    [WorkflowRun]
    public Task RunAsync(CounterState? state = null) => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task<int> IncrementAsync(int amount)
    {
        RecordActivity();
        _state = _state.Increment(amount);
        return Task.FromResult(_state.Count);
    }

    [WorkflowUpdate]
    public async Task<string> IncrementWithAuditAsync(int amount)
    {
        RecordActivity();
        _state = _state.Increment(amount);
        return await Workflow.ExecuteActivityAsync<string>(
            "Audit",
            new object?[] { Workflow.Info.WorkflowId, _state.Count },
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
    }

    [WorkflowUpdate]
    public async Task<int> IncrementSlowlyAsync(int amount)
    {
        RecordActivity();
        _state = _state.Increment(amount);
        await Workflow.DelayAsync(TimeSpan.FromSeconds(4));
        return _state.Count;
    }

    [WorkflowUpdate]
    public Task<int> IncrementSlowlySerializedAsync(int amount) => RunSerializedAsync(async () =>
    {
        RecordActivity();
        _state = _state.Increment(amount);
        await Workflow.DelayAsync(TimeSpan.FromSeconds(1));
        return _state.Count;
    });

    [WorkflowQuery]
    public int GetCount() => _state.Count;
}

// ---------------------------------------------------------------------------
// Scenario B — RollingCounter (low CAN threshold)
// ---------------------------------------------------------------------------

[Workflow]
public interface IRollingCounter : IDurableObject
{
    [WorkflowUpdate] Task<int> IncrementAsync(int amount);
    [WorkflowQuery] int GetCount();
}

[Workflow]
public class RollingCounter : DurableObjectBase, IRollingCounter
{
    private CounterState _state;

    [WorkflowInit]
    public RollingCounter(CounterState? state) => _state = state ?? new CounterState(0);

    [WorkflowRun]
    public Task RunAsync(CounterState? state = null) => DurableObjectRunAsync();

    // Deliberately low threshold so CAN fires quickly in tests.
    protected override bool ShouldContinueAsNew() =>
        base.ShouldContinueAsNew() || Workflow.CurrentHistoryLength >= 30;

    protected override Task<IReadOnlyCollection<object?>> OnBeforeContinueAsNewAsync() =>
        Task.FromResult<IReadOnlyCollection<object?>>(new object?[] { _state });

    [WorkflowUpdate]
    public Task<int> IncrementAsync(int amount)
    {
        RecordActivity();
        _state = _state.Increment(amount);
        return Task.FromResult(_state.Count);
    }

    [WorkflowQuery]
    public int GetCount() => _state.Count;
}

// ---------------------------------------------------------------------------
// Scenario E — TimerCounter (durable recurring timer)
// ---------------------------------------------------------------------------

[Workflow]
public interface ITimerCounter : IDurableObject
{
    [WorkflowQuery] int GetTicks();
}

[Workflow]
public class TimerCounter : DurableObjectBase, ITimerCounter
{
    private int _ticks;

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    protected override Task OnActivateAsync()
    {
        ScheduleTimer("tick", TimeSpan.FromSeconds(1), recurring: true);
        return Task.CompletedTask;
    }

    protected override Task OnTimerAsync(string name)
    {
        if (name == "tick") _ticks++;
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public int GetTicks() => _ticks;
}

// ---------------------------------------------------------------------------
// Scenario F — ScheduledGreeter (self-deactivates each tick)
// ---------------------------------------------------------------------------

[Workflow]
public interface IScheduledGreeter : IDurableObject
{
}

[Workflow]
public class ScheduledGreeter : DurableObjectBase, IScheduledGreeter
{
    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    protected override async Task OnActivateAsync()
    {
        await Workflow.ExecuteActivityAsync<string>(
            "Audit",
            new object?[] { Workflow.Info.WorkflowId, 0 },
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
        Deactivate();
    }
}

// ---------------------------------------------------------------------------
// Scenario G — ReminderTarget (canonical reminder receiver)
// ---------------------------------------------------------------------------

[Workflow]
public interface IReminderTarget : IReminderReceiver
{
    [WorkflowQuery] int GetReminderCount();
    [WorkflowQuery] string GetLastDeliveryId();
}

[Workflow]
public class ReminderTarget : DurableObjectBase, IReminderTarget
{
    private int _count;
    private string _lastDeliveryId = string.Empty;

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task OnReminderAsync(string reminderName, ReminderDeliveryContext context)
    {
        RecordActivity();
        _count++;
        _lastDeliveryId = context.DeliveryId;
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public int GetReminderCount() => _count;

    [WorkflowQuery]
    public string GetLastDeliveryId() => _lastDeliveryId;
}

// ---------------------------------------------------------------------------
// Scenario H — IdlingCounter (stays resident past idle window)
// ---------------------------------------------------------------------------

[Workflow]
public interface IIdlingCounter : IDurableObject
{
    [WorkflowUpdate] Task<int> IncrementAsync(int amount);
    [WorkflowQuery] int GetCount();
}

[Workflow]
public class IdlingCounter : DurableObjectBase, IIdlingCounter
{
    private CounterState _state;

    [WorkflowInit]
    public IdlingCounter(CounterState? state) => _state = state ?? new CounterState(0);

    [WorkflowRun]
    public Task RunAsync(CounterState? state = null) => DurableObjectRunAsync();

    protected override Task<IReadOnlyCollection<object?>> OnBeforeContinueAsNewAsync() =>
        Task.FromResult<IReadOnlyCollection<object?>>(new object?[] { _state });

    [WorkflowUpdate]
    public Task<int> IncrementAsync(int amount)
    {
        RecordActivity();
        _state = _state.Increment(amount);
        return Task.FromResult(_state.Count);
    }

    [WorkflowQuery]
    public int GetCount() => _state.Count;
}

// ---------------------------------------------------------------------------
// Scenario K — TallyMachine (explicit workflow type name via ITallyBox interface)
// ---------------------------------------------------------------------------

[Workflow("TallyMachine")]
public interface ITallyBox : IDurableObject
{
    [WorkflowUpdate] Task<int> AddAsync(int amount);
    [WorkflowQuery] int Total();
}

[Workflow]
public class TallyMachine : DurableObjectBase, ITallyBox
{
    private int _total;

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task<int> AddAsync(int amount)
    {
        RecordActivity();
        _total += amount;
        return Task.FromResult(_total);
    }

    [WorkflowQuery]
    public int Total() => _total;
}

// ---------------------------------------------------------------------------
// Scenario N — GuardedCounter (used by interceptor tests)
// ---------------------------------------------------------------------------

[Workflow]
public interface IGuardedCounter : IDurableObject
{
    [WorkflowUpdate] Task<int> IncrementAsync(int amount);
    [WorkflowUpdate] Task<int> SlowIncrementAsync(int amount);
    [WorkflowUpdate] Task<int> WithdrawAsync(int amount, string token);
    [WorkflowUpdate] Task ThrowingUpdateAsync();
    [WorkflowQuery] int GetCount();
}

[Workflow]
public class GuardedCounter : DurableObjectBase, IGuardedCounter
{
    private int _count;

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task<int> IncrementAsync(int amount)
    {
        RecordActivity();
        _count += amount;
        return Task.FromResult(_count);
    }

    [WorkflowUpdate]
    public async Task<int> SlowIncrementAsync(int amount)
    {
        RecordActivity();
        _count += amount;
        await Workflow.DelayAsync(TimeSpan.FromSeconds(1));
        return _count;
    }

    [WorkflowUpdate]
    public Task<int> WithdrawAsync(int amount, string token)
    {
        RecordActivity();
        _count -= amount;
        return Task.FromResult(_count);
    }

    [WorkflowUpdate]
    public Task ThrowingUpdateAsync()
    {
        // Throws a non-ApplicationFailureException — interceptor must convert to UnhandledUpdateException.
        throw new InvalidOperationException("Simulated unhandled exception in update handler.");
    }

    [WorkflowQuery]
    public int GetCount() => _count;
}

// ---------------------------------------------------------------------------
// Scenario O — FailingOnActivateCounter (OnActivateAsync throws on first activation)
// ---------------------------------------------------------------------------

[Workflow]
public interface IActivationFailureCounter : IDurableObject
{
    [WorkflowQuery] int GetCount();
}

/// <summary>
/// A DurableObject whose OnActivateAsync throws on the first activation, then succeeds
/// on subsequent activations. Uses a static flag keyed by workflow ID to simulate a
/// one-time failure so the test can verify that a new start-with-update after termination
/// creates a clean execution.
/// </summary>
[Workflow]
public class ActivationFailureCounter : DurableObjectBase, IActivationFailureCounter
{
    // Static set tracking which workflow IDs have already failed once.
    // Under the local test server each workflow gets a unique ID, so this is safe across tests.
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool>
        FailedOnce = new();

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    protected override Task OnActivateAsync()
    {
        var id = Workflow.Info.WorkflowId;
        // First activation: fail. Subsequent activations (new run IDs after termination): succeed.
        if (FailedOnce.TryAdd(id, true))
        {
            throw new InvalidOperationException($"Simulated activation failure for '{id}'");
        }

        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public int GetCount() => 0;
}
