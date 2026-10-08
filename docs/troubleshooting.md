# Troubleshooting DurableObjects

Find the symptom below, check the configuration or handler, and apply the targeted fix.

## Symptom: "My worker starts but nothing happens"

**Cause:** The object type is not registered on the worker, or the worker polls a different task
queue. Without `AddDurableObjectWorkflows` (or explicit workflow registration), a worker may start
with other registered types but cannot execute this object. Requests can remain pending or
workflow tasks can fail until a correctly configured worker handles them.

**A missing `[WorkflowRun]` is a different failure:** when the scanner discovers a concrete
`[Workflow]` type with no declared entry point, SDK validation fails during worker setup.
The SDK does not inherit `[WorkflowRun]` from the base class.

**Fix:** Declare an entry point on every concrete object:

```csharp
// Wrong — scanner discovers this type, but SDK validation fails during worker setup.
[Workflow]
public class Counter : DurableObjectBase, ICounter
{
    // Missing a declared [WorkflowRun] method.
}

// Right
[Workflow]
public class Counter : DurableObjectBase, ICounter
{
    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();
}
```

Register the object's assembly on the worker:

```csharp
// Wrong — no workflow types registered here.
services.AddHostedTemporalWorker("my-task-queue");

// Right
services.AddHostedTemporalWorker("my-task-queue")
        .AddDurableObjectWorkflows(typeof(Counter).Assembly);
```

**See also:** [implementation requirements](durable-objects.md#implementation-requirements).

## Symptom: "I'm getting a non-determinism error"

**Possible cause:** `ConfigureAwait(false)` in a handler or lifecycle hook can move continuations
off Temporal's workflow scheduler, causing `InvalidWorkflowSchedulerException` or a replay
mismatch. Other non-deterministic workflow changes can also cause replay errors; see the
[analyzer rules](analyzers.md#general-temporal-rules).

**Fix:** Use bare `await` or `ConfigureAwait(true)` in workflow methods and lifecycle hooks:

```csharp
[WorkflowUpdate]
public async Task IncrementAsync()
{
    var result = await ExecuteActivityAsync(
        (MyActivity a) => a.SomeWorkAsync(),
        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
    // Do not add ConfigureAwait(false) to the await above.
    _count += result;
}
```

Activities and host setup run outside the workflow scheduler and may use `ConfigureAwait(false)`.

**See also:** [workflow scheduler requirements](durable-objects.md#implementation-requirements).

## Symptom: "`DurableObjectNotFoundException` when querying an object"

**Cause:** No execution exists for this object ID, or its history has been purged.

`factory.Get<T>(objectId)` creates a local client without an RPC. Updates use Update-with-Start
and can create a missing object; queries do not. A missing-object query maps the SDK's
not-found error to `DurableObjectNotFoundException`.

**Fix:** Use `GetOrCreateAsync` to start the execution before querying. This confirms existence,
not successful completion of activation:

```csharp
var counter = await factory.GetOrCreateAsync<ICounter>("my-counter");
var count = counter.GetCount();
```

If requests stay pending instead, check that the client and worker task queues match:

```csharp
services.AddDurableObjects("my-task-queue");
services.AddHostedTemporalWorker("my-task-queue")
        .AddDurableObjectWorkflows(typeof(Counter).Assembly);
```

**See also:** [factory operations](durable-objects.md#api-overview).

## Symptom: "My scheduled object runs only once"

**Cause:** A per-tick object never called `Deactivate()`. Schedules default to
`ScheduleOverlapPolicy.Skip`, so later ticks are skipped while the first execution remains open.
Skipped ticks do not themselves grow that execution's history.

**Fix:** Self-complete after the work so the next tick can start a fresh execution:

```csharp
[Workflow]
public class DailyReport : DurableObjectBase, IDailyReport
{
    [WorkflowRun] public Task RunAsync() => DurableObjectRunAsync();

    protected override async Task OnActivateAsync()
    {
        await GenerateReportAsync();
        Deactivate();
    }
}
```

**See also:** [explicit deactivation](tier-model.md).

## Symptom: "`WorkflowUpdateFailedException` in my client code"

**Cause:** The handler failed, or authorization/deactivation rejected the update. Unexpected
handler exceptions become `ApplicationFailureException` with `ErrorType` set to
`"UnhandledUpdateException"`; application failures pass through unchanged. The object stays alive,
but partial state mutations are not rolled back.

**Fix:** Inspect the inner failure for the error type:

```csharp
try
{
    await counter.IncrementAsync();
}
catch (WorkflowUpdateFailedException ex)
    when (ex.InnerException is ApplicationFailureException appEx)
{
    // UnhandledUpdateException, Unauthorized, ObjectDeactivating, or a domain error.
    Console.WriteLine($"Update failed: {appEx.ErrorType} — {appEx.Message}");
}
```

Throw an application failure to report a domain error; validate before mutating:

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

**See also:** [update handler exceptions](failure-handling.md#update-handler-exceptions) and
the [input validation sample](../samples/02-input-validation/).

## Symptom: "I'm getting a `PlatformNotSupportedException` at startup"

NativeAOT is not a supported compatibility target. The notes below describe one known failure
mode; avoiding it does not establish end-to-end compatibility.

**Cause:** The application reached the ungenerated `DispatchProxy` fallback, which needs runtime
code generation unavailable in NativeAOT. The failure occurs when the proxy is first created:

```text
System.PlatformNotSupportedException: Operation is not supported on this platform.
   at System.Reflection.Emit.DynamicMethod...
```

**Fix:** Install `TemporalCommunity.DurableObjects.Analyzers`, keep the contract public, top-level,
and non-generic, and use its generated extension such as `factory.GetCounterClient(id)`.
Resolve `DO0005` diagnostics, which indicate an unsupported contract shape.
If generation is not an option, remove `<PublishAot>true</PublishAot>` or set it to `false`.

Generated clients avoid `DispatchProxy` for supported contracts, but worker discovery and other
SDK features may still need runtime capabilities unavailable in NativeAOT.

**See also:** [generated clients](durable-objects.md#generated-clients).

## Symptom: "Reminder delivery activity is not registered"

**Cause:** `AddDurableObjectWorkflows` registers the dispatcher workflow and interceptor, not
the delivery activity.

**Fix:** Register the activity separately on the worker that executes reminder dispatches:

```csharp
services.AddHostedTemporalWorker("my-task-queue")
        .AddDurableObjectWorkflows(typeof(MyObject).Assembly)
        .AddDurableObjectReminderDelivery<ReminderDeliveryActivities>();
```

Register the activity type in DI before the worker starts. Do not also register the same activity
through `AddSingletonActivities`, `AddTransientActivities`, `AddActivities`, or assembly scanning.

**See also:** [reminders and idempotency](failure-handling.md#reminders-and-idempotency) and
the [scheduling sample](../samples/03-scheduling/).

## Symptom: "`temporal` CLI not in PATH"

Install the [Temporal CLI](https://docs.temporal.io/cli#install) and ensure it is on `PATH` to
start a local dev server for samples. The repository's integration tests use an SDK-managed
test server and do not need a separately installed CLI.
See [sample setup](../samples/README.md#run-durable-objects-demos-0105).

## Still stuck?

Review [failure handling](failure-handling.md) and
[implementation requirements](durable-objects.md#implementation-requirements).
Open a [GitHub issue](https://github.com/temporal-community/temporal-dotnet-extensions/issues)
with the library version, error message, and exception chain (including `ex.InnerException`).
