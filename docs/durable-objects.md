# Durable Objects Concepts

`TemporalCommunity.DurableObjects` is an opinionated durable-actor programming model built on the
Temporal .NET SDK. Temporal remains the durable execution engine; this library adds conventions
for addressing long-lived entities, starting them without a read-then-start race, serializing updates and signals, carrying typed
state, containing handler failures, and managing lifecycle behavior.

## When to use it

| Choose a Durable Object when... | Choose a plain Temporal workflow when... |
|---|---|
| A stable ID represents a long-lived entity. | The execution represents a process with a defined end. |
| Updates should execute turn-by-turn by default. | Handler interleaving or custom concurrency is part of the design. |
| A call should start a missing entity and submit its first update without a read-then-start race. | Starting, signaling, and child workflows should remain explicit. |
| You want framework policies for failures and deactivation. | You need child workflows or unrestricted SDK behavior. |

Durable Objects do not add storage outside Temporal or provide cross-object transactions.
“Resident” means the workflow execution remains open; it does not mean a CLR instance is always
loaded in worker memory.

## Identity and activation

An object ID is its Temporal workflow ID and is globally unique within a Temporal namespace, not
within a task queue. The library never rewrites or prefixes IDs. Applications should adopt an ID
convention such as `account/<id>` or use separate namespaces when types could otherwise collide.

Generated update methods use Update-with-Start. The operation starts the execution when needed and
submits the update without a read-then-start race. The start and successful update are not one
atomic outcome: the workflow may start even if update validation or handling fails.
Generated clients and the reflection proxy also use Signal-with-Start for every signal call,
starting cold or closed objects. Signal receipt does not imply successful activation or handling.
Queries do not start a missing object. Requires Temporal Server 1.30 or later; the SDK remains
pinned to 1.16.0.

On .NET 8 and later, `ListDurableObjectExecutionsAsync<T>()` returns workflow ID, run ID, status,
task queue, timestamps, history length, and schedule origin. It returns running canonical objects
by default; `DurableObjectListOptions` can include scheduled executions and closed runs. The legacy
`ListDurableObjectsAsync<T>()` ID stream remains for compatibility.

### Choosing object granularity

Each object ID identifies one logical Durable Object across its workflow runs, including runs
created by Continue-as-New. That ID determines what is serialized together. With the default
`DurableObjectWorkerOptions.Serialize = true`, updates and signals for one ID share one gate,
including across `await` boundaries. A handler waiting on a slow activity delays later handlers
for that ID. Concurrent client RPCs need not reach the server in the order they were invoked.

Choose an ID around the data whose rules must be checked together, such as one account, order, or
cart. Avoid one object for a whole category, such as `inventory` or `global`; it becomes a single
queue for all callers. History growth and Continue-as-New are also tracked per object, so a busy
object rolls over more often and briefly rejects updates during each rollover (see
[Activation and Continue-as-New Admission](failure-handling.md#activation-and-continue-as-new-admission)).

Operations that span objects, such as moving funds between two accounts, are not atomic. They go
through activities and are at-least-once; see
[Activities and object-to-object calls](#activities-and-object-to-object-calls).

## Lifecycle and state

V1 supports two lifecycle modes:

- **Resident:** the workflow execution remains open while idle. Temporal may evict its sticky-cache
  instance and replay it later without changing the programming model.
- **Explicit deactivation:** `DeactivateAsync()` rejects new updates and drains executing handlers
  before completion. Workflow code can instead call `Deactivate()` after finishing its work.
  A later activation starts fresh; completed state is not automatically restored.

Continue-as-New compacts history when the server suggests it or history reaches
`DurableObjectOptions.MaxHistoryLength`. `DurableObjectBase<TState>` carries a
`DurableObjectSnapshot<TState>` automatically. Applications remain responsible for keeping
`TState` serialization-compatible. Users of non-generic `DurableObjectBase` must explicitly return
constructor arguments from `OnBeforeContinueAsNewAsync()`.

See [Tier Model](tier-model.md) for lifecycle and deactivation details.

## Updates, signals, queries, and failures

Use SDK attributes on contract **and implementation** methods:
`[WorkflowUpdate]` for Task/Task&lt;T&gt;, `[WorkflowSignal]` for Task (not Task&lt;T&gt;), and
`[WorkflowQuery]` for synchronous read-only methods. No new signal attribute is required.

```csharp
[WorkflowSignal]
Task RecordReadingAsync(string eventId, double celsius);
```

Choose signals for notifications where the sender needs receipt, not processing confirmation.
Choose updates for validated commands, results, and caller-visible errors.

| Behavior | Update | Signal | Query |
|---|---|---|---|
| Call completes when | Handler completes or fails | **Server records the signal**; no worker is needed for receipt | Query returns or fails |
| Cold or closed object | Update-with-Start starts it | Signal-with-Start starts it | Does not start it |
| During activation | Waits | Waits | Fails `ObjectNotReady` |
| Serialization (`Serialize=true`) | Shared update/signal gate, held across awaits | Same gate | Not gated; may see partial mutations |
| Continue-as-New admission | New/queued updates rejected with `ObjectContinuingAsNew` | Drained and included in a fresh snapshot before rollover | Served |
| During deactivation | Rejected with `ObjectDeactivating` | **Dropped + Warning** | Existing query policy unchanged |
| Authorization | `Authorize`; rejection returned to caller | `AuthorizeSignal`; denial/error dropped and logged, not returned to sender | None |
| Handler failure | Caller gets update error | Remaining processing abandoned + Error; partial effects persist | Caller gets query error |
| Caller retries after an ambiguous outcome | May duplicate; use application operation IDs | May duplicate; use stable event IDs | Read-only |

Signals have no update validator or processing-result channel. Per-call transport options
(timeout, retry, gRPC metadata, cancellation) apply to the signal RPC, not to handler lifetime.
Cancelling that RPC cannot retract a signal already recorded. gRPC metadata is not automatically
the workflow headers inspected by an authorization callback.

Updates and signals are serialized together by default, including across `await` boundaries. Update validators run before the
handler body. Unexpected handler exceptions are converted into application failures so a rejected
update does not wedge the workflow task. Queries are not serialized: a query can run
while an update or signal is suspended at an `await` and observe state already mutated
but not yet committed as a completed operation. Validate before mutating, or expose a separately
maintained committed-state snapshot when readers require that guarantee. Queries are synchronous
and read-only in the contract; generated clients add asynchronous query methods for callers.

`OnTimerAsync` callbacks also bypass update/signal serialization. If a handler reads `State`, awaits an
activity, and then writes, a timer that fires during the await can change `State`. The update's
write then overwrites the timer's change. To make timer callbacks take turns with updates, wrap
**all** relevant update/signal bodies and the `OnTimerAsync` body in `RunSerializedAsync`. Wrapping only
`OnTimerAsync` does not help: `RunSerializedAsync` uses its own gate, separate from the
interceptor's shared update/signal gate, so an unwrapped handler never holds it.

```csharp
[WorkflowUpdate]
public Task<int> IncrementAsync() => RunSerializedAsync(async () =>
{
    var current = State;
    await ExecuteActivityAsync((AuditActivities a) => a.RecordAsync(current), Options);
    State = current + 1;
    return State;
});

// Waits until IncrementAsync finishes instead of running during its await.
protected override Task OnTimerAsync(string name) => RunSerializedAsync(() =>
{
    State += 100;
    return Task.CompletedTask;
});
```

An accepted update can still be repeated if its response is lost and the application makes a new
call. `DurableObjectCallOptions` does not expose a caller-supplied Temporal update ID, so a new
invocation may have a new ID. Use idempotent handlers or persist an application operation key when
caller retries must not repeat a mutation. Carry deduplication state through Continue-as-New when
it must survive run boundaries.

### Signal authorization, failure boundaries, and observability

Set `DurableObjectWorkerOptions.AuthorizeSignal` to a synchronous deterministic predicate on
`HandleSignalInput`. It may inspect already available arguments/headers, but must not do arbitrary
I/O or asynchronous auth calls. `false` means Warning drop; a callback exception means Error drop.
Registration fails if `Authorize` is configured, a registered DurableObject declares signals,
and `AuthorizeSignal` is missing (including objects registered individually before scanning).
The interceptor also drops named signals with `SignalAuthorizationNotConfigured` if manual
configuration or registration after scanning bypasses that check.

**Dynamic signals are unsupported on DurableObjects**, not just generated contracts.
Registration rejects definitions with `DynamicSignal`, whether dynamic-only or mixed with named
signals, regardless of authorization configuration. At execution the interceptor checks the SDK's
actual selected handler and drops dynamic signals with `DynamicSignalNotSupported` before invoking
authorization or user code. This also covers late workflow registration and handlers installed
through `Workflow.DynamicSignal`. Named handlers in a mixed definition still follow the normal
named-signal rules when startup validation has been bypassed. Do not use dynamic handlers as an
unknown-signal fallback.
Existing update authorization and the old two-argument interceptor constructor remain supported;
the new three-argument constructor carries both callbacks.

Denied signals still occupy history before authorization. Namespace credentials/access control
are the server boundary; application signal auth cannot prevent history growth.

Ordinary and `ApplicationFailureException` handler failures are contained: remaining processing
is abandoned, and the object can keep handling calls. **There is no rollback** of prior State
changes or Activity/external effects. An unrelated `OperationCanceledException` is also logged
and dropped. When `Workflow.CancellationToken` is canceled, that exception propagates with SDK
cancellation semantics. SDK Continue-as-New control exceptions are not swallowed.
This safety net does not contain activation/lifecycle failures, worker crashes, or decoder errors.

An undecodable signal is logged and discarded by SDK 1.16.0 **before** the DurableObject
interceptor, authorization callback, and handler. It has no DurableObject drop event. Unknown
signal names may be buffered by the SDK instead; version wire names and payload types carefully.

Framework drops use replay-aware `Workflow.Logger`, EventId **4101** /
`DurableObjectSignalDropped`, with structured `Category`, `Signal`, and `ErrorType` fields:

| Category | Level |
|---|---|
| `ObjectDeactivating` | Warning |
| `Unauthorized` | Warning |
| `AuthorizationFailure` | Error |
| `SignalHandlerFailure` | Error |
| `SignalAuthorizationNotConfigured` | Error |
| `DynamicSignalNotSupported` | Error |

Only wire identifiers and exception type names are reported; framework signal-drop events do not
include payloads, auth headers, raw exceptions, messages, or stack traces. Preserve the SDK's
workflow/run correlation scopes in your host logger, and avoid secrets in identifiers.
Route these categories and the SDK's decode-error logs through your existing host logging/alert
infrastructure. Prefer nonblocking, bounded, nonthrowing providers. A failing provider is contained
without recursive reporting and cannot defeat signal handler exception containment.
Logs are best effort, **not durable alerts**: ordinary history replay suppresses framework events,
but workflow-task retries/resets/export retries may duplicate them, and crashes/buffering/filtering
may lose them. A history signal event proves receipt, not successful handling or alert delivery.

### Signals and Continue-as-New

At rollover, updates retain their existing admission rejection. Signals cannot reject after
receipt: the run loop drains handlers, snapshots, and repeats if a signal entered user code during
an asynchronous snapshot or handlers remain unfinished. The final check and rollover have no
intervening await. A concurrent server-side signal/close race causes Temporal to retry the workflow
task with the new signal rather than silently close over it.

`PrepareStateForContinueAsNewAsync` / `OnBeforeContinueAsNewAsync` **may run more than once**.
Make snapshot hooks idempotent, including any Activity effects; do not assume one invocation per
rollover. Continuous traffic during asynchronous hooks can delay rollover indefinitely; bound
handler work and apply producer backpressure before reaching server history/signal limits.
Update-only runs take the existing drain/snapshot path once, without new commands or patch markers.
If a signal initiates deactivation during snapshot preparation, deactivation wins: the object
drains and closes rather than rolling over. Closing is not state preservation.

### Append in a signal; process later

The small, compiled [SignalInbox example](snippets/SignalInbox.cs) keeps a bounded Pending list and
recently completed IDs in `State`, validates before mutation, and deduplicates stable event IDs.
Its one recurring timer processes at most 16 items per tick with finite Activity timeouts and
retry attempts. Activity/processing failures are caught **inside** the timer; the item stays queued
and a later tick retries. Escaped timer exceptions are terminal, not caught by signal containment.

The example intentionally avoids a long-held user gate: Enqueue only appends synchronously, the
single timer is the only remover, and it re-reads current State after every Activity await. This
lets new appends proceed during processing without overwriting them. Do not generalize it to
multiple consumers or arbitrary list-mutating updates/reminders. For those, gate all mutators
with `RunSerializedAsync` and keep gate holds bounded so draining/snapshot/deactivation can finish.

The example's overflow/invalid-input policy is handler failure + framework Error log; a sender
cannot learn acceptance from its signal acknowledgement. Deduplication lasts only while an ID is
pending or among the 1,024 retained completion IDs. A poison item blocks the ordered queue; a
production policy can record retry/backoff/quarantine in State. The Activity implementation must
atomically deduplicate its stable event ID with the external effect, or use an idempotent operation:
successful external work with a lost response can be retried. Removing from State is **not** an
external transaction. The tests use a failing Activity stand-in, not a real external dedup store.

Pending work and dedup IDs survive Continue-as-New, **not deactivation, failure, or termination
followed by a fresh start**. Drain/persist before closing if needed. Reminders can restart a closed
object but do not restore its discarded backlog.

See [Failure Handling](failure-handling.md) for client exceptions, authorization, lifecycle-hook
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

Signals do not consume update admission slots, but they still add history and have separate
server quotas. Do not treat signal acknowledgement as backpressure on a bounded application
backlog. Monitor history growth and your deployment's limits; asynchronous snapshot drains may
delay rollover even after the configured history threshold is reached.

Use a dedicated Temporal worker and task queue for Durable Objects in the v1 topology.
`AddDurableObjectWorkflows` installs `DurableObjectWorkerInterceptor` on the whole worker, so its
authorization predicate, update serialization, exception wrapping, and deactivation gate also
apply to unrelated workflow types hosted there. Configure `AddDurableObjects(taskQueue)` with the
same queue that the dedicated worker polls. A supported dedicated Durable Object process connects
to one Temporal namespace. Hosting workers for multiple namespaces in one process is unsupported
as part of the supported worker topology.

Update/signal serialization gives each object actor-like turn behavior (see
[Choosing object granularity](#choosing-object-granularity)). Set `Serialize = false` only when
handlers are safe to overlap across awaits and state access is designed for that. The generated and
compatibility proxy synchronous query methods block a caller thread for a network round trip; use
async query methods from concurrent server code.

Temporal limits how many updates can be in flight at once for one workflow execution; the default
is 10. With `Serialize = true`, updates waiting their turn behind a running update count toward that
limit. When the limit is reached, additional calls fail with
`Temporalio.Exceptions.RpcException` whose `Code` is `ResourceExhausted`. The exception is not
wrapped in `WorkflowUpdateFailedException`. The rejected update did not run, and the object remains
usable. Treat it as a signal to back off and retry, and keep slow work out of update handlers on
hot objects. Cluster operators can configure this limit differently.

Reminders are delivered through a dispatcher workflow, an activity, and Update-with-Start, so
delivery latency and worker backlog matter for frequent reminders. History event count is only one
budget: large payloads and typed snapshots can increase replay and rollover cost before the event
threshold. Measure representative payload size, replay time, snapshot size, and rollover latency.
`GetOrCreateAsync` performs a start RPC; when the next operation is already an update and separate
existence confirmation is unnecessary, compare it with a direct update-with-start path before
adding the extra call. Once Continue-as-New starts, updates that have not entered handler code,
including updates queued behind the serialization gate, are rejected with `ObjectContinuingAsNew`
while running handlers drain; callers can retry them against the next run. Monitor history growth
and rollover frequency for the application's load profile.

See [performance testing](../benchmarks/README.md) for the automated Temporal load runner,
rollover checks under sustained traffic, and BenchmarkDotNet client-overhead benchmarks.

## Generated clients

`TemporalCommunity.DurableObjects.Analyzers` generates a concrete client and factory extension for
every supported public contract. Updates retain their contract names; synchronous queries gain
asynchronous methods, and every generated method has a `DurableObjectCallOptions` overload.

Acquire that concrete client once through an extension such as `GetCounterClient`. Call an update
directly when that operation should materialize a missing object because generated updates use
Update-with-Start. Call `GetOrCreateAsync<T>` first only when the first operation is a query against
a potentially missing object, then query through the generated asynchronous method in service code.

The runtime prefers a registered generated client even when code calls `Get<T>()`. Unsupported or
ungenerated contracts retain the `DispatchProxy` compatibility path. NativeAOT deployments are not
a supported compatibility target for this library. See
[analyzer and generator requirements](analyzers.md).

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

- Register `AddDurableObjects(taskQueue)` on the client side for `IDurableObjectFactory`, and use
  the same task queue for a dedicated Durable Object worker configured with
  `AddDurableObjectWorkflows(assembly)` for workflow discovery and policies. A direct worker uses
  the overload on `TemporalWorkerOptions`.
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

## API overview

### Factory operations

| API | Purpose |
|---|---|
| `Get<T>` | Create a local generated client or compatibility proxy without an RPC. |
| `GetOrCreateAsync<T>` | Ensure the object execution exists, then return its client. |
| `QueryDurableObjectAsync<TResult>` | Perform a non-blocking query by wire name. |
| `QueryOrDefaultAsync<TResult>` | Query and return `default` when the object is absent or inactive. |
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

During activation, updates wait for `OnActivateAsync` to finish while queries fail as
`ObjectNotReady` rather than observing partial state. Before Continue-as-New drains handlers and
prepares state, update admission closes; callers receive the known pre-handler rejection
`ObjectContinuingAsNew`. That rejection is safe to retry against the next run, while unknown
outcomes still require application idempotency. Cancellation from activation and pre-CAN hooks
retains the Temporal SDK's canceled terminal status. `OnDeactivateAsync` remains best-effort.
See [Failure Handling](failure-handling.md#activation-and-continue-as-new-admission) for the stable
error messages and exception details.

## Continue reading

- [Getting Started](getting-started.md)
- [Failure Handling](failure-handling.md)
- [Troubleshooting](troubleshooting.md)
- [Samples](../samples/README.md)
