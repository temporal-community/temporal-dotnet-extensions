# Tier Model

Choose resident execution (Tier 1) for long-lived entities or explicit deactivation (Tier 3)
when an execution should complete. Tier 2 cold passivation is excluded from this release.

---

## Tier 1 — Resident (Default)

A Tier 1 object's workflow execution stays open indefinitely. The run loop parks at
`Workflow.WaitConditionAsync` between activations, consuming no worker compute while idle.
The open execution and its history still exist in Temporal. Temporal's sticky-cache eviction
handles periods of inactivity transparently: the worker may retain a cached workflow instance,
or reconstruct it from event history when work arrives after eviction or restart.

**There is no idle ContinueAsNew and no idle passivation in v1.** An idle Tier 1 object simply
waits. History compaction (ContinueAsNew) is triggered only by the `MaxHistoryLength` threshold
in `DurableObjectOptions` (default 10,000 events) or when `Workflow.ContinueAsNewSuggested`
is true.

To tune history compaction, pass `DurableObjectOptions` to the base constructor.
`MaxHistoryLength` defaults to 10,000 events in this library. Lower values reduce history
replayed per execution but increase rollover frequency; measure replay and rollover latency
for your workload.

**Use Tier 1 when:** the object accumulates state over time and should be addressable at a stable
ID — counters, accounts, player sessions, shopping carts, document state machines.

---

## Tier 3 — Explicit Deactivation

A Tier 3 object closes its workflow execution when deactivated. There are two deactivation paths:

### External deactivation — `DeactivateAsync()`

An external caller invokes `DeactivateAsync()` on the typed client. Because it is a
`[WorkflowUpdate]`, the caller gets confirmation that the deactivation was accepted. The
interceptor then rejects any new updates with `errorType: "ObjectDeactivating"`. The run loop
drains all in-flight handlers, calls `OnDeactivateAsync()`, and completes the execution.

```csharp
var obj = factory.Get<IMyObject>("my-object");
await obj.DeactivateAsync(); // caller confirmed; object draining
```

### Internal deactivation — `Deactivate()`

Workflow code calls the protected `Deactivate()` helper after finishing its work, typically at
the end of `OnActivateAsync`. Unlike `DeactivateAsync()`, this path does not drain update handlers;
do not use it while other handlers still need to finish.

This is required for **scheduled objects** (objects used with `CreateDurableObjectScheduleAsync`).
Without self-deactivation, `ScheduleOverlapPolicy.Skip` suppresses subsequent ticks while the
first execution remains open. See [implementation requirements](durable-objects.md#implementation-requirements).

```csharp
protected override Task OnActivateAsync()
{
    // Do the tick's work here...
    Deactivate(); // self-complete so next tick can run
    return Task.CompletedTask;
}
```

---

## Tier 2 — Cold Passivation (Not Available)

Tier 2 cold passivation is excluded from this release; no API exists. Explicit deactivation
completes an execution, but does not provide state restoration into a later fresh execution.

---

## `ListDurableObjectsAsync` Known Behavior

`IDurableObjectFactory.ListDurableObjectsAsync<T>()` queries Temporal visibility for executions
whose workflow type matches `T` (running only by default). Visibility is eventually consistent — a
just-created object may not appear immediately.

The `netstandard2.1` package asset does not include the Temporal SDK visibility API and throws
`PlatformNotSupportedException` when this method is called. Use the `net8.0` or `net10.0` asset
for visibility enumeration.

The legacy `ListDurableObjectsAsync<T>` ID stream preserves its original behavior and includes
schedule-created executions. Use `ListDurableObjectExecutionsAsync<T>` when classification or
execution metadata matters. Its default excludes scheduled executions with Temporal's built-in
`TemporalScheduledById` visibility attribute—no ID parsing or custom Search Attribute is needed:

```csharp
await foreach (var execution in factory.ListDurableObjectExecutionsAsync<IMyObject>())
{
    Console.WriteLine($"{execution.ObjectId}: {execution.Status}");
}

await foreach (var execution in factory.ListDurableObjectExecutionsAsync<IMyObject>(
                   new DurableObjectListOptions(includeScheduled: true)))
{
    Console.WriteLine(execution.IsScheduled
        ? $"Schedule {execution.ScheduleId}: {execution.ObjectId}"
        : $"Canonical: {execution.ObjectId}");
}
```
