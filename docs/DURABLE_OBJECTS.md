# Durable Objects Concepts

`TemporalCommunity.DurableObjects` is an opinionated durable-actor programming model built on the
Temporal .NET SDK. Temporal remains the durable execution engine; this library adds conventions
for addressing long-lived entities, starting them atomically, serializing updates, carrying typed
state, containing handler failures, and managing lifecycle behavior.

## When to use it

| Choose a Durable Object when... | Choose a plain Temporal workflow when... |
|---|---|
| A stable ID represents a long-lived entity. | The execution represents a process with a defined end. |
| Updates should execute turn-by-turn by default. | Handler interleaving or custom concurrency is part of the design. |
| A call should atomically start a missing entity. | Starting, signaling, and child workflows should remain explicit. |
| You want framework policies for failures and deactivation. | You need signals, child workflows, or unrestricted SDK behavior. |

Durable Objects do not add storage outside Temporal or provide cross-object transactions.
“Resident” means the workflow execution remains open; it does not mean a CLR instance is always
loaded in worker memory.

## Identity and activation

An object ID is its Temporal workflow ID and is globally unique within a Temporal namespace, not
within a task queue. The library never rewrites or prefixes IDs. Applications should adopt an ID
convention such as `account/<id>` or use separate namespaces when types could otherwise collide.

Generated update methods use Update-with-Start. The first update starts the execution when needed,
and concurrent callers race safely against the same workflow ID. Queries do not start a missing
object.

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
validation failures. See [ADR 005](../adr/005-signals-banned.md).

Updates are serialized by default, including across `await` boundaries. Validators run before the
handler body. Unexpected handler exceptions are converted into application failures so a rejected
update does not wedge the workflow task. Queries are read-only and synchronous in the contract;
generated clients add asynchronous query methods for callers.

See [Failure Handling](FAILURE_HANDLING.md) for client exceptions, authorization, lifecycle-hook
failures, deactivation draining, and reminder idempotency.

## Activities and object-to-object calls

Workflow code must perform external I/O through Temporal activities. `DurableObjectBase` provides
`ExecuteActivityAsync` helpers that preserve the workflow scheduler without requiring repeated
static `Workflow` calls or `ConfigureAwait(true)`.

Direct object-to-object workflow calls are not part of v1. Use an activity that receives
`IDurableObjectFactory` and calls the target object. This creates a clear Temporal activity
boundary for external RPC. See the [object-to-object sample](../samples/04-object-to-object/).

## Timers, reminders, and schedules

| Mechanism | Use when |
|---|---|
| `ScheduleTimer` | The current execution needs a durable one-shot or recurring internal timer. |
| `CreateDurableObjectReminderAsync` | A canonical object should receive recurring, idempotent reminder updates. |
| `CreateDurableObjectScheduleAsync` | A Temporal Schedule should create a fresh execution for each periodic job. |

Schedule-created objects must call `Deactivate()` after their work completes so overlap policy does
not suppress later ticks. Reminder receivers should deduplicate using `ReminderDeliveryContext`.
See the [scheduling sample](../samples/03-scheduling/).

## Generated clients and NativeAOT

`TemporalCommunity.DurableObjects.Analyzers` generates a concrete client and factory extension for
every supported public contract. Updates retain their contract names; synchronous queries gain
asynchronous methods, and every generated method has a `DurableObjectCallOptions` overload.

The runtime prefers a registered generated client even when code calls `Get<T>()`. Unsupported or
ungenerated contracts retain the `DispatchProxy` compatibility path. Only the generated client
dispatch path is NativeAOT-compatible; this is not a claim that all Temporal worker discovery is
reflection-free. See [ADR 009](../adr/009-generated-asynchronous-clients.md).

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
- [Required Patterns](BOILERPLATE.md)
- [Failure Handling](FAILURE_HANDLING.md)
- [Troubleshooting](TROUBLESHOOTING.md)
- [Samples](../samples/README.md)
