# Tier Model

DurableObjects have three lifecycle tiers. Tier 1 and Tier 3 ship in v1. Tier 2 is explicitly
deferred — no API surface for it exists in this release.

---

## Tier 1 — Resident (Default)

A Tier 1 object's workflow execution stays open indefinitely. The run loop parks at
`Workflow.WaitConditionAsync` between activations, consuming no server resources when idle.
Temporal's sticky-cache eviction handles periods of inactivity transparently — the worker keeps
a cached copy of the workflow's state in memory; if the worker restarts, the execution resumes
from the event history on the next activation.

**There is no idle ContinueAsNew and no idle passivation in v1.** An idle Tier 1 object simply
waits. History compaction (ContinueAsNew) is triggered only by the `MaxHistoryLength` threshold
in `DurableObjectOptions` (default 10,000 events) or when `Workflow.ContinueAsNewSuggested`
is true.

> **Tuning `MaxHistoryLength`:** The default of 10,000 events matches the Temporal SDK default
> and is appropriate for most objects. For objects with very high update frequency (hundreds of
> updates per minute), consider reducing `MaxHistoryLength` to compact history more aggressively
> and keep individual executions shorter. Pass the value via the `DurableObjectBase` constructor
> parameter (forwarded to `WorkflowRunOptions`) or set it in your workflow options at worker
> registration time. Lowering this value increases the frequency of ContinueAsNew transitions,
> which have a small latency cost but reduce per-execution memory on the worker.

**This is the default.** Every DurableObject that does not call `Deactivate()` or
`DeactivateAsync()` is Tier 1.

**Use Tier 1 when:** the object accumulates state over time and you want it always accessible
at its stable ID — counters, accounts, player sessions, shopping carts, document state machines.

---

## Tier 3 — Explicit Deactivation

A Tier 3 object closes its workflow execution when deactivated. There are two deactivation paths:

### External deactivation — `DeactivateAsync()`

An external caller invokes `DeactivateAsync()` on the typed proxy. Because it is a
`[WorkflowUpdate]`, the caller gets confirmation that the deactivation was accepted. The
interceptor then rejects any new updates with `errorType: "ObjectDeactivating"`. The run loop
drains all in-flight handlers, calls `OnDeactivateAsync()`, and completes the execution.

```csharp
var obj = factory.Get<IMyObject>("my-object");
await obj.DeactivateAsync(); // caller confirmed; object draining
```

### Internal deactivation — `Deactivate()`

The object calls the protected `Deactivate()` helper from inside `OnActivateAsync` (typical) or
any other handler. The run loop exits after the current handler returns.

This is required for **scheduled objects** (objects used with `CreateDurableObjectScheduleAsync`).
Without self-deactivation, `ScheduleOverlapPolicy.Skip` suppresses subsequent ticks while the
first execution remains open. See [BOILERPLATE.md](BOILERPLATE.md) under "Scheduled Objects."

```csharp
protected override Task OnActivateAsync()
{
    // Do the tick's work here...
    Deactivate(); // self-complete so next tick can run
    return Task.CompletedTask;
}
```

---

## Tier 2 — Cold Passivation (Deferred to v1.1)

> **Plain-language summary:** Tier 2 objects are stored in workflow history between activations —
> they save state but incur a cold-start latency cost on each reactivation (the workflow must
> replay history or restore from an external snapshot before handling its first update). Tier 2
> is marked `[Experimental]` and is **not recommended for v1 production use** — it is excluded
> entirely from this release.

Cold passivation serializes an object's state to an external store on deactivation and restores
it on re-activation, allowing the Temporal workflow execution to be closed when the object is
idle and started fresh when needed. This would enable large populations of objects that do not
pay for persistent open executions.

**Tier 2 is fully excluded from v1.** No API surface, no `[Experimental]` stubs. Three problems
block a correct implementation:

1. **Visibility eventual-consistency race.** The activity that queries Temporal visibility to
   locate passivated objects can return stale results — a just-passivated object may appear
   running, and a just-activated object may appear absent. There is no synchronous read-path
   in Temporal visibility; fixing this requires an external store with synchronous read semantics
   (Azure Table, Redis, Cosmos), not Temporal visibility.

2. **Update-ordering bug.** `update-with-start` can dispatch the first update before the
   restoration activity completes inside `DurableObjectRunAsync`. A retry loop in the activity
   does not gate updates from running on unrestored state. Fixing this requires a handler gate
   that blocks all updates until restoration is confirmed complete.

3. **External store dependency.** Cold passivation requires a durable external store not provided
   or configured by this library. Introducing a mandatory external dependency before the API
   stabilizes adds complexity before it is justified.

**v1.1 scope for Tier 2** requires: (a) synchronous-read external store integration, (b) handler
gating in the base run loop that blocks updates until `RestorePassivationStateAsync` completes,
and (c) an integration scenario that kills and restarts a worker between passivation and
reactivation and asserts correct state.

---

## `ListDurableObjectsAsync` Known Behavior

`IDurableObjectFactory.ListDurableObjectsAsync<T>()` queries Temporal visibility for all running
executions whose workflow type matches `T`. Temporal visibility is eventually consistent — a
just-created object may not appear immediately.

The `netstandard2.1` package asset does not include the Temporal SDK visibility API and throws
`PlatformNotSupportedException` when this method is called. Use the `net8.0` or `net10.0` asset
for visibility enumeration.

**Scheduled-object executions appear in results.** `CreateDurableObjectScheduleAsync<T>` spawns
fresh executions per tick; these have the same workflow type as canonical objects but carry a
time-suffixed workflow ID pattern (e.g. `<base-id>-<ISO8601-timestamp>`). This suffix is
predictable and can be used to filter them out of listing results:

```csharp
await foreach (var id in factory.ListDurableObjectsAsync<IMyObject>())
{
    // Scheduled one-shots have a time-suffix; canonical objects do not.
    // Adjust the pattern to match your base ID naming convention.
    if (!IsTimeSuffixed(id))
        yield return id;
}

static bool IsTimeSuffixed(string id) =>
    System.Text.RegularExpressions.Regex.IsMatch(id, @"-\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}");
```

Filtering by ID pattern works for most use cases. For stricter separation, attach a custom
Search Attribute at object creation time and filter on it when listing. A built-in
attribute-tagging option is planned for v1.1.
