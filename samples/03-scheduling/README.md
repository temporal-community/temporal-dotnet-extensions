# Scheduling: fresh jobs and recurring entity updates

| Mechanism | Per tick | Suitable example |
|---|---|---|
| `CreateDurableObjectScheduleAsync` | Starts a fresh workflow with a timestamp-suffixed ID | Publish a periodic report |
| `CreateDurableObjectReminderAsync` | Dispatches an update to the same canonical object ID | Maintain subscription state across recurring reminders |

## Run

Start `temporal server start-dev`, then from the repository root:

```sh
dotnet run --project samples/03-scheduling
```

The demo waits for real ticks, checks at least two distinct report executions and two reminder deliveries, deletes its schedules,
and deactivates the tracker. Inspect the completed reports and subscription-tracker history in
the server UI. A canonical object can change run ID at Continue-as-New while keeping the same
workflow ID and carried state.

## Fresh report execution

`ReportGenerator.OnActivateAsync` invokes `PublishReportAsync` and calls `Deactivate()` after the
activity completes. The execution must complete so the default schedule overlap policy can admit
later ticks. A resident object left open suppresses later ticks while it is still running.

## Stateful reminder receiver

`SubscriptionTracker` uses `DurableObjectBase<SubscriptionState>` to carry its email, completed
delivery keys, and reminder count into each new run. Its handler:

1. Checks the reminder name and `ReminderDeliveryContext.DeliveryId` against completed keys.
2. Runs the notification activity, passing the delivery ID as a downstream idempotency key.
3. Records successful completion and increments the count after the activity returns.

The delivery ID is stable for retries of a dispatcher execution. Temporal update-ID deduplication
is scoped to a run; the carried set protects completed deliveries across rollover, including an
older duplicate arriving after a newer delivery. A downstream service must also honor the key:
an activity may retry after its external effect succeeded. This demo logs a simulated notification,
so it does not claim exactly-once email delivery. Production applications need a retention policy
for completed keys to bound snapshot size.

## Worker registration

Register `ReminderDeliveryActivities` in DI, then use `AddDurableObjectReminderDelivery` to register
both its activity and the dispatcher workflow. `AddSingletonActivities` alone does not register the
dispatcher. The actual sample wiring is:

```csharp
builder.Services.AddSingleton<ReminderDeliveryActivities>();
builder.Services.AddHostedTemporalWorker(TaskQueue)
    .AddDurableObjectWorkflows(typeof(SubscriptionTracker).Assembly)
    .AddDurableObjectReminderDelivery<ReminderDeliveryActivities>()
    .AddSingletonActivities<SchedulingActivities>();
```
