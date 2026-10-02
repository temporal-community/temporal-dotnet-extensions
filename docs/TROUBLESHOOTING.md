# Troubleshooting DurableObjects

Common mistakes developers make with this library, in roughly the order you are likely to
encounter them. Each entry describes what the symptom looks like, what causes it, and how to fix
it.

---

## Symptom: "My worker starts but nothing happens"

**Cause:** Either `[WorkflowRun]` is missing from the concrete class, or `AddDurableObjectWorkflows`
was never called on the worker.

The Temporal SDK uses `IsDefined(WorkflowRunAttribute, inherit: false)` to locate the entry point.
It does not inherit `[WorkflowRun]` from `DurableObjectBase`. If the attribute is missing on your
concrete class, `WorkflowDefinition.Create(type)` throws at worker startup — but if you have
other workflow types registered without the DurableObjects scanner, the worker may still start
successfully and simply ignore your type.

If `AddDurableObjectWorkflows` was not called, your workflow type is not registered at all. The
worker starts, polls the task queue, and silently ignores any updates targeting your object type.
There is no error — updates are left pending in Temporal until a correctly configured worker picks
them up.

**Fix:**

Every concrete DurableObject class needs exactly one line:

```csharp
// Wrong — [WorkflowRun] is on DurableObjectBase, not here. Worker ignores this type.
[Workflow]
public class Counter : DurableObjectBase, ICounter
{
    // [WorkflowRun] missing — WorkflowDefinition.Create throws at startup
    // if AddDurableObjectWorkflows scans this type
}

// Right
[Workflow]
public class Counter : DurableObjectBase, ICounter
{
    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();
}
```

And the worker registration must call `AddDurableObjectWorkflows`:

```csharp
// Wrong — workflow types are not registered
services.AddHostedTemporalWorker("my-task-queue");

// Right
services.AddHostedTemporalWorker("my-task-queue")
        .AddDurableObjectWorkflows(typeof(Counter).Assembly);
```

**See also:** [implementation requirements](DURABLE_OBJECTS.md#implementation-requirements) — implementation requirements

---

## Symptom: "I'm getting a non-determinism error"

**Cause:** `ConfigureAwait(false)` was used inside an update handler, query handler, or lifecycle
hook.

The Temporal .NET SDK runs workflow code on a custom `TaskScheduler` (`WorkflowInstance`) with
`MaximumConcurrencyLevel == 1`. A `WorkflowTracingEventListener` monitors every task scheduled
during an activation and throws `InvalidWorkflowSchedulerException` immediately if a task is
scheduled on the thread pool. `ConfigureAwait(false)` routes `await` continuations to
`TaskScheduler.Default` — the thread pool — instead of back onto the workflow scheduler.

You will see one of:

- An `InvalidWorkflowSchedulerException` thrown immediately (the tracing listener caught it).
- A non-determinism error on replay: the continuation ran at a different point in the command
  sequence than the history recorded, so replaying the same history produces different commands.

**Fix:**

Use `ConfigureAwait(true)` (or omit the call entirely — `true` is the C# default) on every
`await` inside workflow code. This applies to update handlers, query handlers, and all lifecycle
hooks (`OnActivateAsync`, `OnTimerAsync`, `OnBeforeContinueAsNewAsync`, `OnDeactivateAsync`).

```csharp
// Wrong — ConfigureAwait(false) routes continuation to the thread pool
[WorkflowUpdate]
public async Task IncrementAsync()
{
    var result = await ExecuteActivityAsync(
        (MyActivity a) => a.SomeWorkAsync(),
        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) })
        .ConfigureAwait(false); // off-scheduler continuation

    _count += result;
}

// Right — a bare await captures the workflow scheduler. DurableObjectBase provides the
// preferred activity helper, so application code does not need to access Workflow directly.
[WorkflowUpdate]
public async Task IncrementAsync()
{
    var result = await ExecuteActivityAsync(
        (MyActivity a) => a.SomeWorkAsync(),
        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });

    _count += result;
}
```

Activity implementations and worker setup code that run outside `WorkflowInstance` are exempt and
may use `ConfigureAwait(false)`.

**See also:** [implementation requirements](DURABLE_OBJECTS.md#implementation-requirements) — workflow scheduler requirements

---

## Symptom: "`DurableObjectNotFoundException` when calling `Get<T>()`"

**Cause:** The object does not exist yet, or the task queue configured on the factory does not
match the task queue the worker polls.

`factory.Get<T>(objectId)` does not issue any RPC—it creates a local generated client or proxy
fallback. The
exception surfaces on the first actual call (an update or query) when the Temporal server cannot
find the workflow execution. The mapped exception is `DurableObjectNotFoundException`, which
wraps either an `RpcException` with gRPC `NOT_FOUND` status or a `WorkflowNotFoundException`
from the SDK.

Two common causes:

1. **Object not created yet.** `Get<T>` does not start the object. The first update call uses
   `update-with-start` which starts the object automatically, but a query call on a non-existent
   object fails with `DurableObjectNotFoundException`.

2. **Task queue mismatch.** `AddDurableObjects("task-queue-A")` registers the factory with
   `DefaultTaskQueue = "task-queue-A"`, but your worker polls `"task-queue-B"`. The proxy sends
   updates to queue A; no worker is listening there.

**Fix:**

For the "not created yet" case with queries, use `GetOrCreateAsync` to guarantee the object exists
before calling any method:

```csharp
// Wrong — Get<T> does not start the object; a query on a non-existent object throws
var counter = factory.Get<ICounter>("my-counter");
var count = counter.GetCount(); // DurableObjectNotFoundException if object never started

// Right — GetOrCreateAsync starts the object if it is not running, then returns a client
var counter = await factory.GetOrCreateAsync<ICounter>("my-counter");
var count = counter.GetCount(); // safe — object exists
```

For the task queue mismatch, ensure the queue name matches across registration and worker setup:

```csharp
// Client side
services.AddDurableObjects("my-task-queue");

// Worker side — must be the same queue
services.AddHostedTemporalWorker("my-task-queue")
        .AddDurableObjectWorkflows(typeof(Counter).Assembly);
```

**See also:** [`docs/DURABLE_OBJECTS.md#api-overview`](DURABLE_OBJECTS.md#api-overview) — factory operations

---

## Symptom: "My scheduled object never deactivates / workflow history is growing without bound"

**Cause:** `Deactivate()` was not called in `OnActivateAsync` for an object used with
`CreateDurableObjectScheduleAsync`.

Temporal Schedules use `ScheduleOverlapPolicy.Skip` by default: if the previous execution is still
running when the next tick fires, the new tick is dropped. A Tier 1 (resident) object stays open
indefinitely — every tick after the first would be silently skipped. The workflow history of the
first execution grows without bound because the object never closes.

**Fix:**

Call `Deactivate()` at the end of `OnActivateAsync` so the execution self-completes after doing
its work. This lets the next tick start a fresh execution.

```csharp
// Wrong — object stays open; subsequent ticks are silently skipped
[Workflow]
public class DailyReport : DurableObjectBase, IDailyReport
{
    [WorkflowRun] public Task RunAsync() => DurableObjectRunAsync();

    protected override async Task OnActivateAsync()
    {
        await GenerateReportAsync().ConfigureAwait(true);
        // Missing Deactivate() — the next scheduled tick will be dropped
    }
}

// Right — object self-completes; next tick gets a fresh execution
[Workflow]
public class DailyReport : DurableObjectBase, IDailyReport
{
    [WorkflowRun] public Task RunAsync() => DurableObjectRunAsync();

    protected override async Task OnActivateAsync()
    {
        await GenerateReportAsync().ConfigureAwait(true);
        Deactivate(); // self-complete so next tick can run
    }
}
```

**See also:** [implementation requirements](DURABLE_OBJECTS.md#implementation-requirements) — scheduled-object requirements; [`docs/TIER_MODEL.md`](TIER_MODEL.md) — "Tier 3 — Explicit Deactivation"

---

## Symptom: "`WorkflowUpdateFailedException` in my client code"

**Cause:** Your update handler threw an exception (or the framework's safety net caught an
unhandled exception from it). This is not a library bug — it is the SDK's way of propagating
handler failures to callers.

The Temporal SDK wraps your handler's failure as `WorkflowUpdateFailedException`. The underlying
cause is nested inside it. The framework interceptor converts any non-`FailureException` exception
from your handler into `ApplicationFailureException(errorType: "UnhandledUpdateException",
nonRetryable: true)` so the object stays alive. If you threw `ApplicationFailureException` yourself
in the handler, that is carried through unchanged.

The exception chain looks like:

```
WorkflowUpdateFailedException
  └─ InnerException: ApplicationFailureException
               └─ ErrorType: "UnhandledUpdateException" (framework-wrapped)
                  or your own errorType if you threw ApplicationFailureException directly
```

**Fix:**

Catch `WorkflowUpdateFailedException` and inspect `.InnerException` for the inner `ApplicationFailureException`:

```csharp
// Wrong — catches too broadly; loses error type information
try
{
    await counter.IncrementAsync();
}
catch (Exception ex)
{
    // ex is WorkflowUpdateFailedException; the actual details are in ex.InnerException
    Console.WriteLine(ex.Message); // not useful
}

// Right — inspect the cause for domain-specific error type
try
{
    await counter.IncrementAsync();
}
catch (WorkflowUpdateFailedException ex)
    when (ex.InnerException is ApplicationFailureException appEx)
{
    // appEx.ErrorType is "UnhandledUpdateException", "Unauthorized",
    // "ObjectDeactivating", or your own errorType
    Console.WriteLine($"Update failed: {appEx.ErrorType} — {appEx.Message}");
}
```

If you want to propagate a specific error type from inside a handler, throw
`ApplicationFailureException` directly:

```csharp
[WorkflowUpdate]
public Task WithdrawAsync(decimal amount)
{
    if (amount > _balance)
        throw new ApplicationFailureException(
            "Insufficient funds.",
            errorType: "InsufficientFunds",
            nonRetryable: true);

    _balance -= amount;
    return Task.CompletedTask;
}
```

**See also:** [`docs/FAILURE_HANDLING.md`](FAILURE_HANDLING.md) — "Update Handler Exceptions"; [`samples/02-input-validation`](../samples/02-input-validation/)

---

## Symptom: "I'm getting a `PlatformNotSupportedException` at startup"

**Cause:** A NativeAOT application reached the ungenerated `DispatchProxy` fallback.

`DurableObjectProxy<T>` uses `DispatchProxy.Create<T, DurableObjectProxy<T>>()` internally to
generate the typed proxy at runtime. `DispatchProxy` uses `Reflection.Emit`, which the .NET AOT
compiler strips. The failure happens at runtime (when the proxy is first created), not at compile
time — there is no AOT warning.

The error will look similar to:

```
System.PlatformNotSupportedException: Operation is not supported on this platform.
   at System.Reflection.Emit.DynamicMethod...
```

**Fix:** Install `TemporalCommunity.DurableObjects.Analyzers`, keep the contract public, top-level,
and non-generic, and use its generated extension such as `factory.GetCounterClient(id)`. Resolve
any `DO0005` diagnostic; it means the contract cannot safely generate a concrete client.

If generation is not an option, disable NativeAOT for that application:

```xml
<!-- Wrong -->
<PropertyGroup>
    <PublishAot>true</PublishAot>
</PropertyGroup>

<!-- Right — remove the property or set it to false -->
<PropertyGroup>
    <PublishAot>false</PublishAot>
</PropertyGroup>
```

The generated client-dispatch path is verified under NativeAOT in CI. This does not imply that
reflection-based worker discovery or every Temporal SDK feature is NativeAOT-compatible.

**See also:** [generated clients and NativeAOT](DURABLE_OBJECTS.md#generated-clients-and-nativeaot)

---

## Symptom: "Reminder delivery activity is not registered"

**Cause:** `AddDurableObjectWorkflows` registers the `ReminderDispatcher` workflow and installs
the DurableObject interceptor, but it does not register the reminder delivery activity.

**Fix:**

Register the delivery activity separately on the worker that executes reminder dispatches:

```csharp
// AddDurableObjectWorkflows adds the dispatcher workflow and interceptor.
// AddDurableObjectReminderDelivery adds the activity that sends each reminder.
services.AddHostedTemporalWorker("my-task-queue")
        .AddDurableObjectWorkflows(typeof(MyObject).Assembly)
        .AddDurableObjectReminderDelivery<ReminderDeliveryActivities>();
```

The activity type must be registered in DI before the worker starts. Do not also register the same
activity through `AddSingletonActivities`, `AddTransientActivities`, `AddActivities`, or assembly
scanning.

**See also:** [`docs/FAILURE_HANDLING.md`](FAILURE_HANDLING.md) — "Reminders and Idempotency"; [`samples/03-scheduling`](../samples/03-scheduling/)

---

## Symptom: "`temporal` CLI not in PATH" / `just doctor` shows an error

**Cause:** The `temporal` CLI is not installed or not on your PATH.

`just doctor` checks prerequisites and will report if the Temporal CLI is missing. This looks
something like:

```
error: temporal: command not found
```

**The CLI is only required for specific tasks** — it is not needed to run integration tests.

- **Not required for:** `just test`, `just test-unit`, `just test-integration`. Integration tests
  use `WorkflowEnvironment.StartLocalAsync()` from the Temporal .NET SDK, which embeds a local
  Temporal server in the test process. No external Temporal server is needed.
- **Required for:** running samples (`just run-sample`) and any workflow inspection via the
  Temporal Web UI or `temporal workflow` commands. Samples connect to a live Temporal server at
  `localhost:7233`, which you start with `temporal server start-dev`.

**Fix:**

If you only need to run tests, you can ignore the `just doctor` warning. If you need to run
samples or inspect workflow history, install the CLI:

```bash
# macOS (Homebrew)
brew install temporal

# Or download from https://docs.temporal.io/cli#install
```

After installing, verify with:

```bash
temporal --version
just doctor
```

**See also:** [`samples/README.md`](../samples/README.md) — "Common Prerequisites"

---

## Still stuck?

- Check the [`docs/FAILURE_HANDLING.md`](FAILURE_HANDLING.md) — covers all exception types the
  framework produces and when.
- Check [implementation requirements](DURABLE_OBJECTS.md#implementation-requirements) — covers the implementation requirements that are easy to get
  wrong on first use.
- Run `just test-unit` — unit tests do not require a Temporal server and are a fast sanity check
  that your build is correct.
- Open a [GitHub Issue](https://github.com/temporal-community/temporal-dotnet-extensions/issues) with the
  error message, the exception chain (including `ex.InnerException`), and which version of the library you
  are using.
