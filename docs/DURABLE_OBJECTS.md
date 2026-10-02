# Durable Objects Concepts

`TemporalCommunity.DurableObjects` is an opinionated durable-actor programming model built on the
Temporal .NET SDK. Temporal remains the durable execution engine; this library adds conventions
for addressing long-lived entities, starting them without a read-then-start race, serializing updates, carrying typed
state, containing handler failures, and managing lifecycle behavior.

## When to use it

| Choose a Durable Object when... | Choose a plain Temporal workflow when... |
|---|---|
| A stable ID represents a long-lived entity. | The execution represents a process with a defined end. |
| Updates should execute turn-by-turn by default. | Handler interleaving or custom concurrency is part of the design. |
| A call should start a missing entity and submit its first update without a read-then-start race. | Starting, signaling, and child workflows should remain explicit. |
| You want framework policies for failures and deactivation. | You need signals, child workflows, or unrestricted SDK behavior. |

Durable Objects do not add storage outside Temporal or provide cross-object transactions.
“Resident” means the workflow execution remains open; it does not mean a CLR instance is always
loaded in worker memory.

## Identity and activation

An object ID is its Temporal workflow ID and is globally unique within a Temporal namespace, not
within a task queue. The library never rewrites or prefixes IDs. Applications should adopt an ID
convention such as `account/<id>` or use separate namespaces when types could otherwise collide.

Generated update methods use Update-with-Start. The operation starts the execution when needed and
submits the update without a read-then-start race. The start and successful update are not one
atomic outcome: the workflow may start even if update validation or handling fails. Queries do not
start a missing object.

On .NET 8 and later, `ListDurableObjectExecutionsAsync<T>()` returns workflow ID, run ID, status,
task queue, timestamps, history length, and schedule origin. It returns running canonical objects
by default; `DurableObjectListOptions` can include scheduled executions and closed runs. The legacy
`ListDurableObjectsAsync<T>()` ID stream remains for compatibility.

## Lifecycle and state

V1 supports two lifecycle modes:

- **Resident:** the workflow execution remains open while idle. Temporal may evict its sticky-cache
  instance and replay it later without changing the programming model.
- **Explicit deactivation:** a caller invokes `DeactivateAsync()`, or workflow code calls
  `Deactivate()`. In-flight handlers drain, new updates are rejected, and the execution completes.
  A later update can start a fresh execution under the same ID.

Continue-as-New compacts history when the server suggests it or history reaches
`DurableObjectOptions.MaxHistoryLength`. `DurableObjectBase<TState>` carries a
`DurableObjectSnapshot<TState>` automatically. Applications remain responsible for keeping
`TState` serialization-compatible. Users of non-generic `DurableObjectBase` must explicitly return
constructor arguments from `OnBeforeContinueAsNewAsync()`.

See [Tier Model](TIER_MODEL.md) for the complete lifecycle and deactivation semantics.

## Updates, queries, and failures

Contract methods must be `[WorkflowUpdate]` or `[WorkflowQuery]`. Signals are intentionally not
supported because they bypass authorization, do not confirm completion, and cannot report update
validation failures.

Updates are serialized by default, including across `await` boundaries. Validators run before the
handler body. Unexpected handler exceptions are converted into application failures so a rejected
update does not wedge the workflow task. Serialization applies to updates only: a query can run
while an update is suspended at an `await` and observe state that the update has already mutated
but not yet committed as a completed operation. Validate before mutating, or expose a separately
maintained committed-state snapshot when readers require that guarantee. Queries are synchronous
and read-only in the contract; generated clients add asynchronous query methods for callers.

An accepted update can still be repeated if its response is lost and the application makes a new
call. `DurableObjectCallOptions` does not expose a caller-supplied Temporal update ID, so a new
invocation may have a new ID. Use idempotent handlers or persist an application operation key when
caller retries must not repeat a mutation. Carry deduplication state through Continue-as-New when
it must survive run boundaries.

See [Failure Handling](FAILURE_HANDLING.md) for client exceptions, authorization, lifecycle-hook
failures, deactivation draining, and reminder idempotency.

## Activities and object-to-object calls

Workflow code must perform external I/O through Temporal activities. `DurableObjectBase` provides
`ExecuteActivityAsync` helpers that preserve the workflow scheduler without requiring repeated
static `Workflow` calls or `ConfigureAwait(true)`.

Direct object-to-object workflow calls are not part of v1. Use an activity that receives
`IDurableObjectFactory` and calls the target object. The activity may retry after the target accepted
an update but before the activity recorded its completion; use a stable operation key and target
side deduplication, and keep pending/failure state or a compensation policy in the caller. See the
[object-to-object sample](../samples/04-object-to-object/).

## Timers, reminders, and schedules

| Mechanism | Use when |
|---|---|
| `ScheduleTimer` | The current execution needs a durable one-shot or recurring internal timer. |
| `CreateDurableObjectReminderAsync` | A canonical object should receive recurring, idempotent reminder updates. |
| `CreateDurableObjectScheduleAsync` | A Temporal Schedule should create a fresh execution for each periodic job. |

`ScheduleTimer` registrations are in-memory workflow state and are not included automatically in
Continue-as-New arguments. Re-register them from `OnActivateAsync`; if the original timer deadline
must survive rollover, store its absolute due time in carried state and arm it for the remaining
duration. Schedule-created objects must call `Deactivate()` after their work completes so overlap
policy does not suppress later ticks. Reminder receivers should deduplicate using
`ReminderDeliveryContext`.
See the [scheduling sample](../samples/03-scheduling/).

## Worker scope and operating limits

`AddDurableObjectWorkflows` installs `DurableObjectWorkerInterceptor` on the whole Temporal worker.
Its authorization predicate, update serialization, exception wrapping, and deactivation gate also
apply to unrelated workflow types hosted by that worker. Use a dedicated worker when these policies
must be isolated, or make the authorization predicate explicitly allow unrelated workflow updates.

Update serialization gives each object actor-like turn behavior, but an update waiting on a slow
activity can hold up later updates for the same ID. Set `Serialize = false` only when handlers are
safe to overlap across awaits and state access is designed for that. The generated and compatibility
proxy synchronous query methods block a caller thread for a network round trip; use async query
methods from concurrent server code.

Reminders are delivered through a dispatcher workflow, an activity, and Update-with-Start, so
delivery latency and worker backlog matter for frequent reminders. History event count is only one
budget: large payloads and typed snapshots can increase replay and rollover cost before the event
threshold. Measure representative payload size, replay time, snapshot size, and rollover latency.
`GetOrCreateAsync` performs a start RPC; when the next operation is already an update and separate
existence confirmation is unnecessary, compare it with a direct update-with-start path before
adding the extra call. Under sustained update load near the Continue-as-New threshold, handlers
continue to be admitted while the run loop drains. Monitor history growth and define an admission
policy for the application's load profile.

See [performance testing](../benchmarks/README.md) for the automated Temporal load runner,
rollover checks under sustained traffic, and BenchmarkDotNet client-overhead benchmarks.

## Generated clients and NativeAOT

`TemporalCommunity.DurableObjects.Analyzers` generates a concrete client and factory extension for
every supported public contract. Updates retain their contract names; synchronous queries gain
asynchronous methods, and every generated method has a `DurableObjectCallOptions` overload.

The runtime prefers a registered generated client even when code calls `Get<T>()`. Unsupported or
ungenerated contracts retain the `DispatchProxy` compatibility path. Only the generated client
dispatch path is NativeAOT-compatible; this is not a claim that all Temporal worker discovery is
reflection-free. See [analyzer and generator requirements](ANALYZERS.md).

## Implementation requirements

Every concrete object must declare its own `[WorkflowRun]` entry point and delegate to
`DurableObjectRunAsync()`. The SDK does not inherit the entry-point attribute from a base class.
For typed state, use an optional snapshot in both the `[WorkflowInit]` constructor and run method:

```csharp
[WorkflowInit]
public Counter(DurableObjectSnapshot<int>? snapshot = null) : base(snapshot, 0) { }

[WorkflowRun]
public Task RunAsync(DurableObjectSnapshot<int>? snapshot = null) => DurableObjectRunAsync();
```

The constructor and run signatures must match. Factory start paths pass no workflow arguments,
so cold starts must work without a supplied snapshot. Plain instance fields are rebuilt by replay
within a run, but are not automatically carried into a new run by the non-generic base.

- Register `AddDurableObjects(taskQueue)` on the client side for `IDurableObjectFactory`, and
  `AddDurableObjectWorkflows(assembly)` on the worker side for workflow discovery and policies.
  A direct worker uses the overload on `TemporalWorkerOptions`.
- Implement each contract handler with the matching `[WorkflowUpdate]` or `[WorkflowQuery]`
  attribute. If you override `DeactivateAsync`, redeclare `[WorkflowUpdate]`; prefer the
  `OnDeactivateAsync` hook for cleanup.
- Use bare `await` or `ConfigureAwait(true)` in workflow methods and lifecycle hooks. Workflow
  continuations must stay on Temporal's deterministic scheduler. Activities and host code run
  outside that scheduler and may use `ConfigureAwait(false)`.
- Route external I/O through activities. The sample activities log simulated effects; an actual
  database or notification service needs its own retry and idempotency policy.
- A per-tick scheduled object must self-complete with `Deactivate()` after its work. Otherwise
  overlap policy suppresses later ticks while the first execution is still open. Skipped ticks
  do not themselves accumulate workflow history in that execution.

Changing the state schema or emitted workflow commands for existing executions requires a
compatibility plan. Replay representative histories before deployment, keep deserialization
compatible with existing snapshots, and use Temporal's workflow versioning APIs where needed.
See [replay verification](MAINTAINER_VERIFICATION.md#replay-compatibility).

## API overview

### Factory operations

| API | Purpose |
|---|---|
| `Get<T>` | Create a local generated client or compatibility proxy without an RPC. |
| `GetOrCreateAsync<T>` | Ensure the object execution exists, then return its client. |
| `QueryDurableObjectAsync<TResult>` | Perform a non-blocking query by wire name. |
| `QueryOrDefaultAsync<TResult>` | Query and return `default` when the object is absent. |
| `ListDurableObjectExecutionsAsync<T>` | Enumerate rich visibility metadata on .NET 8+. |
| `CreateDurableObjectReminderAsync<T>` | Deliver recurring reminders to a canonical object. |
| `CreateDurableObjectScheduleAsync<T>` | Start a fresh scheduled execution for each tick. |

`DurableObjectCallOptions` carries caller cancellation, RPC timeout, retry behavior, and gRPC
metadata. Cancellation stops waiting for the client RPC; it does not cancel an update Temporal has
already accepted.

### Lifecycle hooks

| Hook | Purpose |
|---|---|
| `OnActivateAsync` | Initialize deterministic execution state and timers. |
| `OnDeactivateAsync` | Perform deterministic cleanup after update draining. |
| `OnTimerAsync` | Handle timers created through `ScheduleTimer`. |
| `OnBeforeContinueAsNewAsync` | Return explicit carry-forward arguments for the non-generic base. |
| `PrepareStateForContinueAsNewAsync` | Normalize typed state before snapshot carry-forward. |

## Continue reading

- [Getting Started](GETTING_STARTED.md)
- [Failure Handling](FAILURE_HANDLING.md)
- [Troubleshooting](TROUBLESHOOTING.md)
- [Samples](../samples/README.md)
