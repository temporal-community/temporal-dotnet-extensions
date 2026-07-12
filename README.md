# TemporalCommunity.DurableObjects

An opinionated durable-actor programming model built on the
[Temporal .NET SDK](https://github.com/temporalio/sdk-dotnet). It gives entity-style workflows
safe defaults for atomic activation, serialized updates, contained update failures, and managed
lifecycle behavior. Temporal supplies the durable execution and replay model; this library adds
the actor conventions and guardrails.

Use DurableObjects for long-lived entities addressed by stable ID, such as accounts, carts,
devices, sessions, and counters. Use a plain Temporal workflow for a process with a defined start
and end, orchestration-heavy control flow, child workflows, signals, or direct access to the full
Temporal SDK surface.

> **Requires Temporal Server v1.28.0 or later** (Update-with-Start GA).
> **Targets .NET 10.0, .NET 8.0, and .NET Standard 2.1.** .NET 8+ receives the full feature set;
> the .NET Standard fallback does not support `ListDurableObjectsAsync`.
> **NativeAOT is not supported in v1** — see [ADR 002](adr/002-dispatchproxy-not-sourcegen.md).

## Installation

```
dotnet add package TemporalCommunity.DurableObjects
```

## Quick Start

Five steps from zero to a working DurableObject. Calling code uses a typed object contract while
the library manages the Temporal start/update dispatch.

### Step 1: Define the contract

Every DurableObject starts with an interface extending `IDurableObject`. Decorate it with
`[Workflow]` (the Temporal SDK uses this to derive the workflow type name). Updates are
fire-and-confirm (`[WorkflowUpdate]`); queries are synchronous reads (`[WorkflowQuery]`).
Updates can include input validation and activity calls.

```csharp
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

[Workflow]
public interface ICounter : IDurableObject
{
    /// <summary>Adds <paramref name="amount"/> to the count. Must be positive.</summary>
    [WorkflowUpdate] Task IncrementAsync(int amount);
    [WorkflowQuery]  int  GetCount();
}
```

```csharp
using Temporalio.Activities;

public sealed class CounterActivities
{
    /// <summary>
    /// Records an increment. In production: write to a database, emit a metric, publish an event.
    /// Activities run outside the workflow — they can do real I/O.
    /// </summary>
    [Activity]
    public Task RecordIncrementAsync(string counterId, int amount, int newTotal)
    {
        Console.WriteLine($"[audit] {counterId}: +{amount} → total {newTotal}");
        return Task.CompletedTask;
    }
}
```

### Step 2: Implement it

Extend `DurableObjectBase` and implement your interface. The one mandatory boilerplate is
`[WorkflowRun] public Task RunAsync() => DurableObjectRunAsync();` — the SDK does not inherit
`[WorkflowRun]` from the base class, so every concrete DurableObject must declare it.
See [BOILERPLATE.md](docs/BOILERPLATE.md) for the full explanation.
The `[WorkflowUpdateValidator]` runs before the handler body;
`DurableObjectBase.ExecuteActivityAsync` is how workflow code triggers external I/O.

```csharp
#pragma warning disable CA1822 // Workflow methods must be instance methods
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

[Workflow]
public sealed class Counter : DurableObjectBase, ICounter
{
    private int _count;

    // Required on every concrete DurableObject — Temporal does not inherit [WorkflowRun].
    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    // Validator: runs synchronously BEFORE IncrementAsync.
    // Throw here to reject the update before the handler body executes.
    [WorkflowUpdateValidator(nameof(IncrementAsync))]
    public void ValidateIncrementAsync(int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
    }

    [WorkflowUpdate]
    public async Task IncrementAsync(int amount)
    {
        _count += amount;

        // External I/O belongs in activities. DurableObjectBase keeps the call in the
        // workflow context; do not use ConfigureAwait(false) in workflow code.
        await ExecuteActivityAsync(
            (CounterActivities act) =>
                act.RecordIncrementAsync(WorkflowId, amount, _count),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });
    }

    [WorkflowQuery]
    public int GetCount() => _count;
}
```

### Step 3: Register on the worker

`AddDurableObjects` registers `IDurableObjectFactory` in DI (client-side).
`AddDurableObjectWorkflows` scans the assembly and registers the workflow types on the worker
(worker-side). Both go in the same service configuration; they serve different roles.

```csharp
// Using Temporalio.Extensions.Hosting
services.AddTemporalClient(opts => opts.TargetHost = "localhost:7233");

// Client-side: registers IDurableObjectFactory with DefaultTaskQueue = "my-task-queue"
services.AddDurableObjects("my-task-queue");

// Worker-side: scans the assembly, registers DurableObject workflow types and activities
services.AddHostedTemporalWorker("my-task-queue")
        .AddDurableObjectWorkflows(typeof(Counter).Assembly)
        .AddSingletonActivities<CounterActivities>();
```

Without `AddSingletonActivities`, no worker can pick up the activity; the update eventually
fails according to its activity timeout and retry policy.

### Step 4: Call it

Inject `IDurableObjectFactory`, get a typed proxy, and call methods. The object ID remains a
Temporal workflow ID, while the proxy handles update-with-start and query dispatch.

```csharp
using Temporalio.Exceptions;

public class MyService(IDurableObjectFactory factory)
{
    public async Task RunAsync()
    {
        // GetOrCreateAsync starts the object if it doesn't exist, then returns a typed proxy.
        ICounter counter = await factory.GetOrCreateAsync<ICounter>("my-counter");

        await counter.IncrementAsync(5);  // validator passes; activity records the increment
        int count = counter.GetCount();   // 5

        try
        {
            await counter.IncrementAsync(-1);
        }
        catch (WorkflowUpdateFailedException)
        {
            // Expected: the validator rejects this before the handler body runs.
        }

        count = counter.GetCount(); // still 5; the object remains alive
    }
}
```

`WorkflowUpdateFailedException` is thrown on the caller side; the object remains alive. Inspect
`ApplicationFailureException.ErrorType` in the chain. See [FAILURE_HANDLING.md](docs/FAILURE_HANDLING.md)
for the full taxonomy.

### Step 5: Verify

```
dotnet run
```

The counter object starts on first update. Its state is recovered from Temporal history after
worker restarts and compatible re-deployments. For state that must cross Continue-as-New, derive
from `DurableObjectBase<TState>` as shown in the Getting Started sample; the non-generic base
continues to support explicit carry-forward through `OnBeforeContinueAsNewAsync()`.

### What happens when an update throws?

If an update handler throws an unexpected exception, `DurableObjectWorkerInterceptor` catches it
and converts it to an `ApplicationFailureException` so the object does not wedge permanently.
On the calling side, the exception chain is: `WorkflowUpdateFailedException` wraps an
`ApplicationFailureException`, which carries the original error in `InnerException`. Inspect
`ApplicationFailureException.ErrorType` for domain-specific error codes (e.g. `"Unauthorized"`,
`"UnhandledUpdateException"`). See [FAILURE_HANDLING.md](docs/FAILURE_HANDLING.md) for the full
exception taxonomy, lifecycle hook failure behavior, and authorization patterns.

---

## Key Concepts

### Tier model

Objects have three lifecycle tiers. V1 ships Tier 1 and Tier 3. See [TIER_MODEL.md](docs/TIER_MODEL.md).

| Tier | Name | Behavior |
|------|------|----------|
| 1 | Resident | Stays open indefinitely; Temporal sticky-cache handles idle periods. **Default.** |
| 2 | Cold Passivation | Deferred to v1.1 — not in this release. |
| 3 | Explicit Deactivation | Caller or object itself calls `DeactivateAsync()` / `Deactivate()` to close. |

#### Deactivation semantics

When deactivation is triggered (via `DeactivateAsync()` from outside, or `Deactivate()` from
inside a handler), the object drains before closing:

- **In-flight updates complete.** Any update handler already executing when deactivation is
  signaled runs to completion before the object closes. New updates arriving after the signal is
  received are rejected with `errorType: "ObjectDeactivating"`.
- **Reactivation is always possible.** After an object deactivates, the next `GetOrCreate` or
  `update-with-start` call on the same ID spins up a fresh execution under
  `WorkflowIdReusePolicy.AllowDuplicate`. The object effectively restarts clean.
- **Drain timeout is not configurable in v1.** The drain waits on
  `Workflow.WaitConditionAsync(() => Workflow.AllHandlersFinished)` with no deadline. Design
  update handlers to complete promptly; avoid long-running blocking operations inside handlers.

#### ContinueAsNew and state preservation

ContinueAsNew is triggered automatically when `Workflow.CurrentHistoryLength` exceeds the
`MaxHistoryLength` threshold (default 10,000 events) or when the Temporal server sets
`Workflow.ContinueAsNewSuggested`. `DurableObjectBase<TState>` carries its protected `State` in a
typed `DurableObjectSnapshot<TState>` automatically. The concrete workflow declares matching
optional snapshot parameters on its `[WorkflowInit]` constructor and `[WorkflowRun]` method.
Applications remain responsible for serialization-compatible evolution of `TState`.

The non-generic `DurableObjectBase` does not infer which fields are state. Objects using it must
override `OnBeforeContinueAsNewAsync()` and keep its argument shape compatible with the workflow
initializer. Otherwise mutable fields reset after Continue-as-New.

### Object identity and namespace scope

> **Object IDs are globally unique within a Temporal namespace — not per-task-queue.**
> Two workers registered on different task queues in the same namespace cannot both own an object
> with the same ID unless they are both registered for the same workflow type. If a second worker
> issues `GetOrCreate` for an ID that is already owned by a different workflow type, Temporal will
> route the start to the existing execution's task queue (the one it was originally started on),
> not the caller's queue. Use distinct ID namespacing conventions (e.g., prefixes) to avoid
> accidental cross-type collisions.

### Reminders vs Schedules

Two mechanisms trigger recurring behavior in DurableObjects — they serve different roles:

| Mechanism | Trigger origin | Use when |
|-----------|---------------|----------|
| **Reminder** (`CreateDurableObjectReminderAsync`) | Fires from inside the object via `AddDurableObjectReminderDelivery` — the object receives `OnTimerAsync` on a canonical, persistent execution | You want the object itself to wake up on a recurring basis (heartbeats, expiry checks). |
| **Schedule** (`CreateDurableObjectScheduleAsync`) | Externally managed Temporal Schedule that triggers `OnTimerAsync` on a *fresh* execution per tick | You want a periodic job that self-deactivates after each run (batch jobs, reports). |

The key difference: reminders keep one long-lived object alive and deliver updates to it;
schedules spin up a new execution per tick and expect that execution to call `Deactivate()` on
completion.

### DurableObject vs plain Temporal workflow

| Choose a DurableObject when... | Choose a plain Temporal workflow when... |
|---|---|
| A stable ID represents a long-lived entity. | The execution represents a process with a defined end. |
| Updates should be serialized across `await` boundaries by default. | Handler interleaving or direct concurrency control is part of the design. |
| Calls should atomically start the entity when it is not running. | Starting, signaling, and child-workflow relationships should be explicit. |
| You want the framework's update failure and deactivation policies. | You need signals, child workflows, or the unrestricted SDK surface. |

DurableObjects do not add persistence outside Temporal or provide cross-object transactions.
“Resident” means the workflow execution remains open; it does not mean a CLR object is always
loaded in worker memory. Typed interface queries are synchronous and park the calling thread;
use `QueryDurableObjectAsync` on asynchronous or high-throughput call paths.

### Signals are banned — use updates

All methods on a DurableObject interface must be `[WorkflowUpdate]` or `[WorkflowQuery]`.
`[WorkflowSignal]` is banned in v1, including on framework-provided methods. Signals bypass
the authorization hook, give callers no confirmation, and offer no rollback on partial state
mutation. See [ADR 005](adr/005-signals-banned.md).

### NativeAOT limitation

`DispatchProxy` (used internally for the typed proxy) uses `Reflection.Emit`. NativeAOT strips
this at publish time. Publishing a DurableObject worker with `PublishAot=true` will fail at
runtime. A source-generator-based proxy is planned for v1.1. See [ADR 002](adr/002-dispatchproxy-not-sourcegen.md).

---

## API Reference

### `IDurableObjectFactory` methods

| Method | Description |
|--------|-------------|
| `Get<T>(objectId)` | Returns a typed proxy. No RPC issued — creation is local. Uses `DefaultTaskQueue`. |
| `Get<T>(objectId, taskQueue)` | Same, with explicit task queue override. |
| `GetOrCreateAsync<T>(objectId)` | Guarantees the object exists (starts if not running) then returns a proxy. Issues one RPC. |
| `GetOrCreateAsync<T>(objectId, taskQueue)` | Same, with explicit task queue override. |
| `QueryDurableObjectAsync<TResult>(objectId, queryName, args)` | Non-blocking async query. Use on hot paths where thread-parking is unacceptable. |
| `QueryOrDefaultAsync<TResult>(objectId, queryName, args)` | Same, but returns `default` instead of throwing when the object is absent. |
| `ListDurableObjectsAsync<T>(runningOnly)` | Async stream of object IDs from Temporal visibility on .NET 8+; unavailable on the .NET Standard fallback. |
| `CreateDurableObjectScheduleAsync<T>(...)` | Temporal Schedule that activates a fresh execution per tick. |
| `CreateDurableObjectReminderAsync<T>(...)` | Temporal Schedule that delivers recurring reminders to a canonical object. |

### `DurableObjectBase` lifecycle hooks

Override any of these in your class. All have no-op defaults.

| Hook | When called | Exception behavior |
|------|-------------|-------------------|
| `OnActivateAsync()` | Once per execution start (including post-ContinueAsNew). | Non-`ApplicationFailureException` → workflow terminates cleanly. |
| `OnDeactivateAsync()` | After drain completes on deactivation. | Swallowed and logged — deactivation completes regardless. |
| `OnTimerAsync(name)` | When a durable timer registered with `ScheduleTimer` fires. | Non-`ApplicationFailureException` → workflow terminates cleanly. |
| `OnBeforeContinueAsNewAsync()` | Just before ContinueAsNew; return carry-forward constructor args. | Non-`ApplicationFailureException` → workflow terminates cleanly. |

`DurableObjectBase<TState>` adds a protected `State` property and automatically carries it through
Continue-as-New in `DurableObjectSnapshot<TState>`. Use `PrepareStateForContinueAsNewAsync` only
when state needs deterministic normalization before the next execution.

### `DurableObjectWorkerInterceptor` options

Pass via `AddDurableObjectWorkflows(assembly, options)`:

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Serialize` | `bool` | `true` | Non-reentrant by default — handlers execute turn-based. |
| `Authorize` | `Func<HandleUpdateInput, bool>?` | `null` (allow all) | Return `false` to reject an update with `errorType: "Unauthorized"`. |

The interceptor is installed automatically by `AddDurableObjectWorkflows`. It also wraps
arbitrary update handler exceptions into clean `ApplicationFailureException` rejections so the
object does not wedge permanently. See [FAILURE_HANDLING.md](docs/FAILURE_HANDLING.md).

---

## Failure Handling

Exception mapping, update handler failure taxonomy, lifecycle hook failure behavior, authorization
patterns, and reminder idempotency — see [FAILURE_HANDLING.md](docs/FAILURE_HANDLING.md).

---

## Versioning

Long-lived DurableObjects accumulate workflow history. Changes to handler names, signatures, and
ContinueAsNew constructor schemas require care. See [ADR 004](adr/004-versioning-strategy.md)
for the full versioning strategy: what is safe, what requires `Workflow.Patched`, and how to
deploy new versions safely.

---

## Object-to-Object Communication

Direct DurableObject-to-DurableObject messaging is deferred from v1. The v1 workaround is an
Activity that calls the target object via `ITemporalClient`:

```csharp
[Activity]
public class CallCounterActivity(IDurableObjectFactory factory)
{
    public async Task<int> GetRemoteCountAsync(string targetId)
    {
        var counter = factory.Get<ICounter>(targetId);
        return counter.GetCount();
    }
}
```

Execute it from inside an update handler:
```csharp
[WorkflowUpdate]
public async Task SyncFromRemoteAsync(string remoteId)
{
    _count = await ExecuteActivityAsync(
        (CallCounterActivity a) => a.GetRemoteCountAsync(remoteId),
        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
}
```

Three candidate designs (Activity-mediated, child workflows, Nexus) are under evaluation for
v1.1. See [ADR 003](adr/003-do-to-do-messaging-deferred.md).

---

## Samples

Six runnable samples covering the core features are in `samples/`. See [samples/README.md](samples/README.md)
for the full index. Each sample requires a live Temporal server (`temporal server start-dev`).

```bash
just run-sample                    # 01-getting-started (default)
just run-sample 03-scheduling      # specific sample by directory name
just run-sample-all                # list all available samples
```

---

## Building from Source

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download), [just](https://github.com/casey/just).

```bash
dotnet tool restore     # install minver-cli and reportgenerator
just build              # restore + compile (Release)
just test               # unit + integration tests
just pack               # produces artifacts/packages/*.nupkg
just doctor             # verify all prerequisites
```

Run `just` with no arguments to list all available recipes.

### Common recipes

| Recipe | What it does |
|--------|-------------|
| `just build` | Restore and compile in Release mode. |
| `just test-unit` | Unit tests only — no Temporal server needed. |
| `just test-integration` | 15 integration scenarios (uses embedded `WorkflowEnvironment`). |
| `just test-filter "FullyQualifiedName~ScenarioA"` | Run a specific scenario. |
| `just pack` | Pack the library; MinVer reads the git tag for the version. |
| `just run-sample` | Run the sample app (requires a live Temporal server at `localhost:7233`). |
| `just ci` | Full CI pipeline: clean → build → unit tests → pack and consumer verification. |

---

## Documentation

- [Getting Started guide](docs/GETTING_STARTED.md) — reading order, document map, and quick links for new developers.
- [Boilerplate guide](docs/BOILERPLATE.md) — required patterns every DurableObject must follow.
- [Failure handling](docs/FAILURE_HANDLING.md) — exception taxonomy, authorization, and reminder idempotency.
- [Tier model](docs/TIER_MODEL.md) — lifecycle tiers and ContinueAsNew behavior.
- [Troubleshooting](docs/TROUBLESHOOTING.md) — common mistakes and how to fix them.
- [Temporal analyzers](docs/ANALYZERS.md) — opt-in compile-time determinism and DurableObjects contract checks.
- [ADRs](adr/) — maintainer-facing architectural decisions and design rationale.

---

## Contributing

1. Fork and clone.
2. `dotnet tool restore` once after cloning.
3. Run `just ci` before opening a PR — this is what CI runs.
4. For integration tests: `just test-integration` (no external Temporal server needed; it uses
   `WorkflowEnvironment.StartLocalAsync()`).
5. Add an ADR in `adr/` for any architectural decision, API surface change, or significant
   constraint. Follow the format in the existing ADRs.

---

## License

MIT — see [LICENSE](LICENSE).
