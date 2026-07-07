# Boilerplate Guide

This document covers the recurring patterns that every DurableObject implementation must follow,
explains why each exists, and shows the common mistakes and how to avoid them.

---

## `[WorkflowRun]` Is Required on Every Concrete Class

The Temporal SDK uses `IsDefined(WorkflowRunAttribute, inherit: false)` to find the workflow
entry point. It does not walk base class declarations. This means `[WorkflowRun]` on
`DurableObjectBase` would not be found — every concrete subclass must declare it.

**Required boilerplate (one line per class):**

```csharp
[WorkflowRun]
public Task RunAsync() => DurableObjectRunAsync();
```

`DurableObjectRunAsync()` is the protected method on `DurableObjectBase` that owns the full
object lifecycle. You delegate to it; do not implement the loop yourself.

**Missing `[WorkflowRun]` fails at worker startup** — `WorkflowDefinition.Create(type)` throws
at registration time with a clear message. You will not reach runtime with this misconfigured.

---

## `DeactivateAsync` Override Rule

`DurableObjectBase.DeactivateAsync()` carries `[WorkflowUpdate]`. The SDK scans the concrete
type with `IsDefined(attr, inherit: false)`. If a subclass overrides `DeactivateAsync` without
redeclaring `[WorkflowUpdate]`, the SDK throws at registration time:

```
WorkflowUpdate on base definition of method but not override
```

**If you override, redeclare the attribute:**

```csharp
[WorkflowUpdate]         // required — do not omit
public override Task DeactivateAsync()
{
    // custom logic
    return base.DeactivateAsync();
}
```

**Prefer `OnDeactivateAsync()` instead.** It is the designed extension point for deactivation-
time behavior. Override `DeactivateAsync` only when you need to change the deactivation protocol
itself (rare).

```csharp
// Preferred — no attribute footgun
protected override Task OnDeactivateAsync()
{
    // cleanup logic
    return Task.CompletedTask;
}
```

---

## `AddDurableObjects` vs `AddDurableObjectWorkflows`

These two calls are easy to confuse. They serve different roles and both are required.

| Call | Side | Role |
|------|------|------|
| `services.AddDurableObjects("my-task-queue")` | **Client** | Registers `IDurableObjectFactory` in DI. Sets `DefaultTaskQueue`. |
| `builder.AddDurableObjectWorkflows(assembly)` | **Worker** | Scans assembly, registers workflow types, installs interceptor. |

```csharp
// Client setup
services.AddTemporalClient(opts => opts.TargetHost = "localhost:7233");
services.AddDurableObjects("my-task-queue");           // IDurableObjectFactory → DI

// Worker setup
services.AddHostedTemporalWorker("my-task-queue")
        .AddDurableObjectWorkflows(typeof(MyObject).Assembly); // scans + registers types
```

A non-hosted (direct) worker uses the `TemporalWorkerOptions` overload:

```csharp
var workerOptions = new TemporalWorkerOptions("my-task-queue");
workerOptions.AddDurableObjectWorkflows(typeof(MyObject).Assembly);
```

---

## Scheduled Objects

Objects used with `CreateDurableObjectScheduleAsync<T>` must self-deactivate after each tick.
Temporal Schedules use `ScheduleOverlapPolicy.Skip` by default: if the previous execution is
still running when the next tick fires, the tick is dropped. A Tier 1 (resident) object stays
open indefinitely — every tick after the first would be dropped.

**Call `Deactivate()` at the end of `OnActivateAsync`** so the execution completes and the next
tick can start a fresh one:

```csharp
[Workflow]
public class DailyReport : DurableObjectBase, IDailyReport
{
    [WorkflowRun] public Task RunAsync() => DurableObjectRunAsync();

    protected override async Task OnActivateAsync()
    {
        // Do the scheduled work
        await GenerateReportAsync();

        // Self-complete — required for scheduled objects
        Deactivate();
    }
}
```

Without `Deactivate()`, subsequent scheduled ticks are silently dropped. This is the most
common mistake when using schedule-based activation.

---

## Every Interface Method Needs a Matching Handler

`ValidateInterface<T>()` (called at `factory.Get<T>()` time) checks the attribute matrix:
every `Task`-returning method must have `[WorkflowUpdate]`, every synchronous-value-returning
method must have `[WorkflowQuery]`, and no method may carry `[WorkflowSignal]`. Violations
surface as `InvalidOperationException` at proxy creation time.

However, `ValidateInterface<T>()` does not cross-check that every method declared on the
interface has a corresponding registered handler on the concrete class. A missing handler fails
at first RPC call with an unregistered-update error from the Temporal SDK.

**Rule:** every `[WorkflowUpdate]` and `[WorkflowQuery]` method on your interface must be
implemented on the concrete class with the matching attribute.

```csharp
[Workflow]
public interface ICounter : IDurableObject
{
    [WorkflowUpdate] Task IncrementAsync();
    [WorkflowQuery]  int  GetCount();
}

[Workflow]
public class Counter : DurableObjectBase, ICounter
{
    private int _count;
    [WorkflowRun] public Task RunAsync() => DurableObjectRunAsync();

    [WorkflowUpdate] public Task IncrementAsync() { _count++; return Task.CompletedTask; }
    [WorkflowQuery]  public int  GetCount() => _count;
    // Both methods implemented — no missing handlers
}
```

---

## `ConfigureAwait(true)` Is Load-Bearing

The Temporal .NET SDK runs workflow code on a custom `TaskScheduler` (`WorkflowInstance`) with
`MaximumConcurrencyLevel == 1`. A `WorkflowTracingEventListener` monitors every task scheduled
during an activation and throws `InvalidWorkflowSchedulerException` immediately if a task is
scheduled on a different scheduler.

`ConfigureAwait(false)` posts continuations to `TaskScheduler.Default` (the thread pool) —
off the workflow scheduler. This produces either:
- **Immediate failure:** the tracing listener detects the off-scheduler task.
- **Silent replay divergence:** the continuation runs after the SDK has already snapshotted
  commands, causing non-determinism on replay.

`ConfigureAwait(true)` (or no call at all, since `true` is the C# default) captures
`TaskScheduler.Current`, which inside a workflow is `WorkflowInstance`. The continuation queues
back onto the same scheduler and runs deterministically.

**Use `ConfigureAwait(true)` on every `await` in workflow code** — update handlers, query
handlers, and lifecycle hooks. This is not a stylistic choice; it is required for correctness.

```csharp
[WorkflowUpdate]
public async Task IncrementAsync()
{
    var result = await Workflow.ExecuteActivityAsync(
        (MyActivity a) => a.SomeWorkAsync(),
        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) })
        .ConfigureAwait(true); // load-bearing

    _count += result;
}
```

Activity implementations and worker infrastructure code that run outside `WorkflowInstance` are
exempt and may use `ConfigureAwait(false)`.
