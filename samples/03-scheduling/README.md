# Sample 03 — Scheduling: Schedule vs Reminder Patterns

This sample shows two distinct scheduling mechanisms in the DurableObjects library:

| | Per-tick Schedule | Canonical Reminder |
|---|---|---|
| **Mechanism** | Temporal Schedule | Temporal Schedule + ReminderDispatcher |
| **Per tick** | Fresh workflow execution | Update delivered to same execution |
| **State** | Resets each tick | Accumulates across all ticks |
| **Workflow ID** | `<base-id>-<timestamp>` | `<base-id>` (constant) |
| **Self-deactivate** | Required (`Deactivate()`) | Optional |
| **Use when** | Periodic batch jobs | Stateful recurring work |

---

## Pattern 1: Per-tick Schedule (`ReportGenerator`)

Each tick of the Temporal Schedule creates a **fresh** workflow execution. The object activates,
does its work, and calls `Deactivate()` to self-complete.

```csharp
protected override async Task OnActivateAsync()
{
    await ExecuteActivityAsync(
        (SchedulingActivities act) => act.PublishReportAsync(WorkflowId),
        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });

    // Self-deactivate: the run loop exits after OnActivateAsync returns.
    // Without this, ScheduleOverlapPolicy.Skip suppresses the next tick
    // while this execution is still open.
    Deactivate();
}
```

**What you see in Temporal Web UI:**
Each tick creates a separate completed execution, e.g.:
- `report-gen-2026-07-08T10:00:00Z`
- `report-gen-2026-07-08T10:00:10Z`
- `report-gen-2026-07-08T10:00:20Z`

**When to use:** Periodic batch jobs, report generation, data exports — any work that should
start from a clean slate each time and has no need to accumulate state across ticks.

---

## Pattern 2: Canonical Reminder (`SubscriptionTracker`)

The reminder pattern delivers each tick as an **update-with-start** to the canonical object
identified by a fixed workflow ID. All deliveries land on the **same execution**, so state
accumulates across ticks.

```csharp
[WorkflowUpdate]
public async Task OnReminderAsync(string reminderName, ReminderDeliveryContext ctx)
{
    // ctx.DeliveryId is stable across retries for the same tick, and unique across ticks.
    // Use it to detect and skip duplicate deliveries that can occur after ContinueAsNew.
    if (_lastDeliveryIds.TryGetValue(reminderName, out var lastId) && lastId == ctx.DeliveryId)
        return; // duplicate — skip

    _lastDeliveryIds[reminderName] = ctx.DeliveryId;
    _reminderCount++;

    await ExecuteActivityAsync(
        (SchedulingActivities act) => act.NotifySubscriberAsync(_email, ctx.DeliveryId),
        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
}
```

**What `ctx.DeliveryId` is for:**

The `DeliveryId` is the dispatcher workflow's ID. It has two useful properties:
1. **Stable across retries** — if the delivery activity retries for the same tick, the
   `DeliveryId` is identical, so the Temporal server deduplicates on the update ID.
2. **Unique across ticks** — different ticks produce different dispatcher executions, so
   `DeliveryId` changes per tick.

After a `ContinueAsNew`, the new execution has no record of prior update IDs from the server's
perspective. The `DeliveryId` check in `OnReminderAsync` is the application-level guard against
processing the same tick twice across the ContinueAsNew boundary.

**What you see in Temporal Web UI:**
One running execution with the fixed ID, e.g. `subscription-tracker-demo`. Each reminder
delivery appears as an update event in the workflow history — not a new execution.

**When to use:** Subscription heartbeats, recurring state updates, objects that need to
accumulate history or state across many ticks (e.g., counting deliveries, tracking last-seen
timestamps).

> **Production note:** `SubscriptionTracker`'s instance fields (`_reminderCount`, `_email`,
> etc.) reset to defaults if the object crosses a `ContinueAsNew` boundary. In production,
> override `OnBeforeContinueAsNewAsync()` to return the state you want to carry forward, and
> add a `[WorkflowInit]` constructor to rehydrate it. See `docs/BOILERPLATE.md` for the full
> pattern.

---

## Worker setup

The reminder pattern requires `AddDurableObjectReminderDelivery` in addition to
`AddDurableObjectWorkflows`:

```csharp
builder.Services.AddHostedTemporalWorker(TaskQueue)
    .AddDurableObjectWorkflows(Assembly.GetExecutingAssembly())
    .AddDurableObjectReminderDelivery<ReminderDeliveryActivities>()
    .AddAllActivities<SchedulingActivities>();
```

`AddDurableObjectReminderDelivery` registers the `ReminderDeliveryActivities` that the
`ReminderDispatcher` workflow uses to deliver updates to the target object. Without it, the
dispatcher workflow will fail when it tries to execute the delivery activity.

> **Common mistake — reminder delivery registration**
>
> Do NOT use `AddSingletonActivities<ReminderDeliveryActivities>()` for the reminder dispatcher.
> Use `AddDurableObjectReminderDelivery<ReminderDeliveryActivities>()` instead.
>
> `AddSingletonActivities<T>()` registers the activity class but does not wire the reminder
> routing. Using it will result in reminder deliveries silently failing — no error at startup,
> just failed workflow tasks in the Temporal Web UI.

---

## Running the sample

1. Start Temporal (e.g. `temporal server start-dev`)
2. `dotnet run --project samples/03-scheduling`
3. Open http://localhost:8233 and observe:
   - Multiple completed executions for the report generator (per-tick pattern)
   - One running execution for the subscription tracker with growing update history (canonical pattern)
