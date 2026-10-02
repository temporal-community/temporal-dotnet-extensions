# Sample 04 — Object-to-Object Communication (Activity Bridge Pattern)

This sample demonstrates the v1 pattern for one DurableObject communicating with another:
the **activity bridge**.

---

## Why direct DO-to-DO calls are not supported in v1

DurableObject workflow methods must be **deterministic** — they are replayed from history on
every worker restart. A direct call to another workflow (DO-to-DO) would:

1. **Break determinism**: the result of a live RPC is not deterministic and cannot be replayed
   from history safely.
2. **Risk deadlocks**: if Object A waits for Object B, and Object B waits for Object A,
   neither can progress.
3. **Complicate fan-out**: a single workflow update calling many others creates unbounded
   in-flight handler chains with no natural backpressure.

---

## The activity bridge pattern

The solution is to route the cross-object call through a Temporal activity:

```
OrderProcessor (workflow update)
  -> DurableObjectBase.ExecuteActivityAsync<FulfillmentActivities>
     -> FulfillmentActivities.ReserveInventoryAsync (activity — runs outside workflow scheduler)
        -> IDurableObjectFactory.Get<IInventoryTracker>
           -> InventoryTracker.ReserveStockAsync (workflow update on the target object)
```

The activity is the **non-deterministic boundary**. Temporal records the activity result in
workflow history, so replays of `OrderProcessor` see the same recorded result rather than
re-issuing the RPC.

---

## Code walkthrough

### OrderProcessor (the caller)

```csharp
[WorkflowUpdate]
public async Task PlaceOrderAsync(string orderId, string productId, int quantity)
{
    State.Statuses[orderId] = "Pending";

    // Cross into the activity boundary — the bridge.
    await ExecuteActivityAsync(
        (FulfillmentActivities act) => act.ReserveInventoryAsync(orderId, productId, quantity),
        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
    State.Statuses[orderId] = "Fulfilled";
}
```

### FulfillmentActivities (the bridge)

```csharp
[Activity]
public async Task ReserveInventoryAsync(string orderId, string productId, int quantity)
{
    // Safe here: activities run outside workflow scheduler — DI calls are allowed.
    var inventory = _factory.Get<IInventoryTracker>("global-inventory");
    await inventory.ReserveStockAsync(orderId, productId, quantity).ConfigureAwait(false);
}
```

The caller must reuse a stable `orderId` when retrying a request. `InventoryTracker` stores each
operation ID and applies a reservation only once, so an activity retry after an accepted update
does not reserve stock twice. `OrderProcessor` records `Pending`, `Fulfilled`, or `Failed` status;
production systems should define how failed orders are retried or compensated. Both objects use
typed snapshots so order status, reservations, and deduplication keys survive Continue-as-New.
A production system also needs a retention policy for operation keys.

`IDurableObjectFactory` is injected via DI, registered by `AddDurableObjects(TaskQueue)` in
`Program.cs`. This sample intentionally exercises the compatibility fallback: the `Get<T>` call is
a local `DispatchProxy` construction (no RPC); `ReserveStockAsync`
issues the actual update RPC.

---

## Benefits of the activity bridge

| Benefit | Explanation |
|---|---|
| **Retryable** | Temporal retries activities with the same arguments; the target update deduplicates by stable order ID |
| **Auditable** | Each bridge call appears as `ActivityTaskScheduled` + `ActivityTaskCompleted` in the caller's workflow history |
| **Determinism-safe** | The non-deterministic DI/RPC call happens inside the activity, not the workflow |
| **Observable** | The caller records pending and terminal order status; applications choose retry or compensation policy |
| **Traceable** | Distributed traces (e.g., via OpenTelemetry) span the activity boundary |

---

## Tracing the call chain in Temporal Web UI

Open http://localhost:8233 after running the demo:

1. **OrderProcessor** workflow (`order-001`): workflow history shows two `ActivityTaskScheduled`
   events — one per `PlaceOrderAsync` call. Each activity event shows the input arguments
   (orderId, productId, quantity) and the completion status.

2. **InventoryTracker** workflow (`global-inventory`): workflow history shows two
   `WorkflowExecutionUpdateAccepted` events — one per `ReserveStockAsync` call from the
   activity bridge.

The full call chain is auditable from either side: OrderProcessor's history shows when the
activity was dispatched and completed; InventoryTracker's history shows when each update arrived.

---

## Running the sample

1. Start Temporal (e.g. `temporal server start-dev`)
2. `dotnet run --project samples/04-object-to-object`
3. Observe two fulfilled orders; a repeated order ID leaves inventory totals at 5 and 3
4. Check Temporal Web UI to trace the call chain across both objects
