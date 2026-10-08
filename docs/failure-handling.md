# Failure Handling

Use this guide to handle client exceptions, lifecycle failures, authorization, and reminder
retries without losing state consistency.

---

## Client-Side Exception Mapping

Generated clients and the compatibility proxy map SDK-level errors to the library's exception
hierarchy before propagating them to callers.

| SDK exception / condition | Mapped to | When |
|--------------------------|-----------|------|
| `RpcException` with gRPC `NOT_FOUND` status | `DurableObjectNotFoundException` | Workflow history has been purged or never existed. |
| `WorkflowNotFoundException` | `DurableObjectNotFoundException` | Lookup by workflow ID returns no execution. |
| `WorkflowQueryRejectedException` | `DurableObjectNotActiveException` | Object has closed/terminated and a query was attempted with `QueryRejectCondition` set. |
| Query against a closed/completed execution (`WORKFLOW_EXECUTION_ALREADY_COMPLETED`) | `DurableObjectNotActiveException` | Object is deactivated and not resident. |
| All other exceptions | Re-thrown as-is | Network errors, `WorkflowUpdateFailedException` (user logic errors), `OperationCanceledException`, etc. |

`WorkflowUpdateFailedException` is intentionally NOT mapped. It carries the application's own
`ApplicationFailureException` from inside an update handler and is meaningful to callers. Inspect
`InnerException` for an `ApplicationFailureException` and read its `ErrorType` for domain-specific
error codes.

---

## Update Handler Exceptions

The table below shows raw SDK behavior when an update handler throws. The interceptor modifies
the last row — see the note following the table.

| Exception from update handler | Raw SDK behavior |
|-------------------------------|-----------------|
| `ApplicationFailureException` (either `nonRetryable` value) | Update fails cleanly. Caller sees `WorkflowUpdateFailedException`. **Object stays alive.** |
| `OperationCanceledException` | Update fails cleanly as cancellation. **Object stays alive.** |
| Any other exception | Fails the **workflow task**, retried indefinitely. Update stays pending. **Object permanently wedged.** |

**Framework safety net:** `DurableObjectWorkerInterceptor.HandleUpdateAsync` catches anything
that is not `Temporalio.Exceptions.FailureException` (the SDK base type, of which
`ApplicationFailureException` is the primary subclass) or `OperationCanceledException` and
rethrows it as `ApplicationFailureException(errorType: "UnhandledUpdateException", nonRetryable: true)`.
This converts the "permanently wedged" outcome into a clean caller-visible update failure. The
caller sees `WorkflowUpdateFailedException`; the object stays alive.

**No state rollback.** Partial mutations made before the throw persist. Write handlers that
validate all inputs before mutating state, so a throw leaves the object in a consistent state.

---

## Lifecycle Hook Exceptions

Lifecycle hooks (`OnActivateAsync`, `OnTimerAsync`, `OnBeforeContinueAsNewAsync`) run in the
**workflow run path** — not inside an update handler. When they throw:

- A non-`ApplicationFailureException` is wrapped as `ApplicationFailureException(errorType:
  "ActivationFailure" | "TimerFailure" | "ContinueAsNewFailure", nonRetryable: true)`.
- The **workflow execution fails**, rather than retrying a broken workflow task indefinitely.
- Any updates pending at the time of activation failure (including the triggering update from
  `update-with-start`) fail with the workflow termination rather than receiving a clean
  `UpdateResponse.Rejected`. Callers see the workflow terminated, not a friendly exception.

If `OnBeforeContinueAsNewAsync` throws, Continue-as-New does **not** proceed. Validate state
preparation before the hook returns; a failure ends the execution instead of carrying state
into a new run.

**`OnDeactivateAsync` is different:** exceptions are swallowed and logged. Deactivation must
complete regardless of cleanup failures.

### Activation failure and recovery

After an `OnActivateAsync` failure, `GetOrCreateAsync` can start a fresh execution under the
same object ID: it explicitly sets `WorkflowIdReusePolicy.AllowDuplicate` and uses an existing
execution if one is running. The new execution does not restore the failed execution's state.
Correct the activation failure before retrying.

---

## Authorization and Framework Updates

If you register an `authorize` predicate on `DurableObjectWorkerInterceptor`, the predicate
receives every update, including those delivered internally by the framework (currently only
`"OnReminder"` — the update delivered by `ReminderDeliveryActivities`).

`DurableObjectWorkerInterceptor.FrameworkUpdateNames` is a `FrozenSet<string>` containing the
wire names of all framework-internal updates. Use it in your predicate to allow them explicitly:

```csharp
services.AddHostedTemporalWorker("my-task-queue")
        .AddDurableObjectWorkflows(
            typeof(MyObject).Assembly,
            new DurableObjectWorkerOptions
            {
                Authorize = input =>
                    DurableObjectWorkerInterceptor.FrameworkUpdateNames.Contains(input.Update)
                    || MyUserAuthCheck(input)
            });
```

The SDK strips the trailing `"Async"` suffix from default update names.
`"OnReminder"` is the wire name for `OnReminderAsync`.
`"Deactivate"` is **not** in `FrameworkUpdateNames` — it is user-initiated and should go through
your `authorize` predicate like any other update.

### Single-tenant vs multi-tenant threat model

`FrameworkUpdateNames.Contains()` in the `authorize` predicate is safe only when **every client
in the namespace is trusted**. Transport authentication alone does not establish that trust.
Wire names are predictable from the library source —
any client with valid namespace credentials can invoke `"OnReminder"` and skip your auth check.

### Multi-tenant authorization

> **Illustrative pseudo-code.** The code below shows the structural approach. A production
> implementation requires a custom outbound interceptor on the `ITemporalClient` used by
> `ReminderDeliveryActivities`. The exact header encoding and secret provisioning are
> application-specific; no built-in helper exists in v1.

For multi-tenant deployments, use a shared-secret header approach instead:

1. Configure `ReminderDeliveryActivities` with an `ITemporalClient` whose outbound interceptor
   stamps a shared secret in a custom header:

   ```csharp
   // Inside ClientOutboundInterceptor.StartUpdateWithStartWorkflowAsync:
   input.Headers["x-framework-secret"] = encodedSecret;
   ```

2. The inbound `authorize` predicate verifies the header rather than checking the update name:

   ```csharp
   Authorize = input =>
   {
       if (input.Headers.TryGetValue("x-framework-secret", out var secret))
           return VerifySecret(secret);
       return MyUserAuthCheck(input);
   }
   ```

The secret must be provisioned to both the delivery worker and the target worker. No built-in
authentication helper is provided.

---

## Reminders and Idempotency

`AddDurableObjectWorkflows(...)` registers the `ReminderDispatcher` workflow and installs the
DurableObject interceptor. It does **not** register the reminder delivery activity. If the worker
delivers reminders, register the activity separately with
`AddDurableObjectReminderDelivery<ReminderDeliveryActivities>()` (or the overload that accepts an
instance). See [the scheduling sample](../samples/03-scheduling/Program.cs). Do not register the
same activity twice.

Reminders are delivered at-least-once. The `ReminderDeliveryActivities.DeliverReminderAsync`
activity derives a stable `UpdateId` from `ActivityExecutionContext.Current.Info.WorkflowId`
(the dispatcher workflow's ID, unique per tick under `ScheduleOverlapPolicy.Skip`), combined
with the target object ID and reminder name:

```
UpdateId = "{dispatcherWorkflowId}:{targetObjectId}:{reminderName}"
```

The Temporal server deduplicates on `WorkflowUpdateOptions.Id`. A retry of the same activity
re-issues the same ID; within the same target run, it does not execute the handler again.

The second argument to `OnReminderAsync` is `ReminderDeliveryContext`, which carries the
`DeliveryId` (the dispatcher workflow's ID). Handlers should retain processed delivery IDs
to detect and skip duplicates:

```csharp
private readonly HashSet<string> _seenDeliveries = [];

[WorkflowUpdate]
public Task OnReminderAsync(string reminderName, ReminderDeliveryContext context)
{
    if (_seenDeliveries.Contains(context.DeliveryId))
        return Task.CompletedTask; // duplicate — skip

    // ... process reminder successfully ...
    _seenDeliveries.Add(context.DeliveryId);
    return Task.CompletedTask;
}
```

### Known limitation: UpdateId dedup across ContinueAsNew

`UpdateId` deduplication in Temporal is per workflow execution (per run ID). If the target
DurableObject performs a ContinueAsNew between a failed reminder delivery activity and its retry,
the new execution has no record of the prior `UpdateId` — the reminder re-delivers as a fresh
update. This is the expected at-least-once behavior. The `DeliveryId` pattern above is the
correct way to achieve at-most-once processing in the presence of CAN boundaries.

Carry processed IDs in typed state (or explicit Continue-as-New arguments); the instance field
in the example alone does not survive rollover. Bound retention according to your retry window.
External side effects still require idempotency at the activity or destination. Record an ID
only after successful processing; an update failure does not roll back a prematurely recorded ID.

---

## Deactivation Drain Protocol

`DeactivateAsync()` confirms the request, not completed cleanup. During draining, the
interceptor rejects updates that have not begun handling with:

```
ApplicationFailureException(errorType: "ObjectDeactivating", nonRetryable: true)
```

The caller sees `WorkflowUpdateFailedException`. Handlers already executing can finish before
cleanup and completion. Do not wait for `Workflow.AllHandlersFinished` inside a handler: that
wait includes the handler itself and cannot complete.

---

## Versioning Quick Reference

Before changing live workflows, replay representative histories and keep snapshot and
Continue-as-New argument deserialization compatible. Changes to emitted workflow commands
need a versioning strategy such as `Workflow.Patched`. Handler removal or renaming also needs
a migration plan for clients and pending updates; an unchanged method name alone does not make
a change replay-safe. See [implementation requirements](durable-objects.md#implementation-requirements).

---

## See Also

For common startup mistakes, exception chain patterns, and environment setup issues, see
[Troubleshooting](troubleshooting.md).
