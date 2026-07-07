# TemporalCommunity.DurableObjects

An object-based programming model built on top of the [Temporal .NET SDK](https://github.com/temporalio/sdk-dotnet).
Write stateful, durable actors backed by Temporal workflows — without managing workflow plumbing directly.

> **Requires Temporal Server v1.28.0 or later** (Update-with-Start GA).
> **Requires .NET 10.0.**
> **NativeAOT is not supported in v1** — see [ADR 002](docs/adr/002-dispatchproxy-not-sourcegen.md).

## Installation

```
dotnet add package TemporalCommunity.DurableObjects
```

## Quick Start

Five steps from zero to a working DurableObject. No raw Temporal concepts in calling code.

### Step 1: Define the contract

Every DurableObject starts with an interface extending `IDurableObject`. Decorate it with
`[Workflow]` (the Temporal SDK uses this to derive the workflow type name). Updates are
fire-and-confirm (`[WorkflowUpdate]`); queries are synchronous reads (`[WorkflowQuery]`).

```csharp
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

[Workflow]
public interface ICounter : IDurableObject
{
    [WorkflowUpdate] Task IncrementAsync();
    [WorkflowQuery]  int  GetCount();
}
```

### Step 2: Implement it

Extend `DurableObjectBase` and implement your interface. The one mandatory boilerplate is
`[WorkflowRun] public Task RunAsync() => DurableObjectRunAsync();` — the SDK does not inherit
`[WorkflowRun]` from the base class, so every concrete DurableObject must declare it.
See [BOILERPLATE.md](docs/BOILERPLATE.md) for the full explanation.

```csharp
[Workflow]
public class Counter : DurableObjectBase, ICounter
{
    private int _count;

    // Required boilerplate — see docs/BOILERPLATE.md
    [WorkflowRun] public Task RunAsync() => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task IncrementAsync()
    {
        _count++;
        return Task.CompletedTask;
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

// Worker-side: scans the assembly, registers DurableObject workflow types
services.AddHostedTemporalWorker("my-task-queue")
        .AddDurableObjectWorkflows(typeof(Counter).Assembly);
```

### Step 4: Call it

Inject `IDurableObjectFactory`, get a typed proxy, and call methods. No task queue, no workflow
ID management, no SDK ceremony.

```csharp
public class MyService(IDurableObjectFactory factory)
{
    public async Task RunAsync()
    {
        ICounter counter = factory.Get<ICounter>("my-counter");

        await counter.IncrementAsync();   // starts the object if not running; delivers the update
        int count = counter.GetCount();   // 1
    }
}
```

### Step 5: Verify

```
dotnet run
```

The counter object starts on first call and persists across restarts. State survives worker
crashes, ContinueAsNew history compaction, and re-deployments.

---

## Key Concepts

### Tier model

Objects have three lifecycle tiers. V1 ships Tier 1 and Tier 3. See [TIER_MODEL.md](docs/TIER_MODEL.md).

| Tier | Name | Behavior |
|------|------|----------|
| 1 | Resident | Stays open indefinitely; Temporal sticky-cache handles idle periods. **Default.** |
| 2 | Cold Passivation | Deferred to v1.1 — not in this release. |
| 3 | Explicit Deactivation | Caller or object itself calls `DeactivateAsync()` / `Deactivate()` to close. |

### DurableObject vs plain Temporal workflow

Use a DurableObject when you want an always-accessible stateful actor by stable ID — the mental
model is a grain or entity, not a process. Use a plain Temporal workflow when the execution has
a defined start and end with complex branching logic, or when you need child workflows, signals,
or other SDK primitives not exposed by the DurableObject API surface.

### Signals are banned — use updates

All methods on a DurableObject interface must be `[WorkflowUpdate]` or `[WorkflowQuery]`.
`[WorkflowSignal]` is banned in v1, including on framework-provided methods. Signals bypass
the authorization hook, give callers no confirmation, and offer no rollback on partial state
mutation. See [ADR 005](docs/adr/005-signals-banned.md).

### NativeAOT limitation

`DispatchProxy` (used internally for the typed proxy) uses `Reflection.Emit`. NativeAOT strips
this at publish time. Publishing a DurableObject worker with `PublishAot=true` will fail at
runtime. A source-generator-based proxy is planned for v1.1. See [ADR 002](docs/adr/002-dispatchproxy-not-sourcegen.md).

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
| `ListDurableObjectsAsync<T>(runningOnly)` | Async stream of object IDs from Temporal visibility. |
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
ContinueAsNew constructor schemas require care. See [ADR 004](docs/adr/004-versioning-strategy.md)
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
    _count = await Workflow.ExecuteActivityAsync(
        (CallCounterActivity a) => a.GetRemoteCountAsync(remoteId),
        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
}
```

Three candidate designs (Activity-mediated, child workflows, Nexus) are under evaluation for
v1.1. See [ADR 003](docs/adr/003-do-to-do-messaging-deferred.md).

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
| `just ci` | Full CI pipeline: clean → build → unit tests → pack. |

---

## Contributing

1. Fork and clone.
2. `dotnet tool restore` once after cloning.
3. Run `just ci` before opening a PR — this is what CI runs.
4. For integration tests: `just test-integration` (no external Temporal server needed; it uses
   `WorkflowEnvironment.StartLocalAsync()`).
5. Add an ADR in `docs/adr/` for any architectural decision, API surface change, or significant
   constraint. Follow the format in the existing ADRs.

---

## License

MIT — see [LICENSE](LICENSE).
