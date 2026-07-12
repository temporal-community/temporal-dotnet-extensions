# Failure Handling

This document covers every failure scenario in `TemporalCommunity.DurableObjects`: how SDK
exceptions surface through the client layer, what happens when update handlers or lifecycle hooks
throw, how the authorization predicate interacts with framework-internal updates, and how reminder
delivery achieves at-least-once semantics with idempotent deduplication.

---

## Client-Side Exception Mapping

`DurableObjectProxy<T>` wraps every call to `ExecuteUpdateWithStartWorkflowAsync` and `QueryAsync`
and maps SDK-level errors to the custom exception hierarchy before propagating to callers.

| SDK exception / condition | Mapped to | When |
|--------------------------|-----------|------|
| `RpcException` with gRPC `NOT_FOUND` status | `DurableObjectNotFoundException` | Workflow history has been purged or never existed. |
| `WorkflowNotFoundException` | `DurableObjectNotFoundException` | Lookup by workflow ID returns no execution. |
| `WorkflowQueryRejectedException` | `DurableObjectNotActiveException` | Object has closed/terminated and a query was attempted with `QueryRejectCondition` set. |
| Query against a closed/completed execution (`WORKFLOW_EXECUTION_ALREADY_COMPLETED`) | `DurableObjectNotActiveException` | Object is deactivated and not resident. |
| All other exceptions | Re-thrown as-is | Network errors, `WorkflowUpdateFailedException` (user logic errors), `OperationCanceledException`, etc. |

`WorkflowUpdateFailedException` is intentionally NOT mapped. It carries the application's own
`ApplicationFailureException` from inside an update handler and is meaningful to callers. Inspect
`innerException.ApplicationFailureException.ErrorType` for domain-specific error codes.

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
- This hits the SDK's `RunTopLevelAsync` → `FailWorkflowExecution` path — the **workflow
  execution terminates cleanly**.
- Any updates pending at the time of activation failure (including the triggering update from
  `update-with-start`) fail with the workflow termination rather than receiving a clean
  `UpdateResponse.Rejected`. Callers see the workflow terminated, not a friendly exception.

**`OnBeforeContinueAsNewAsync` behavior:** if this hook throws, the framework wraps the exception
as `ApplicationFailureException(errorType: "ContinueAsNewFailure", nonRetryable: true)`. This
terminates the workflow execution — ContinueAsNew does **not** proceed. Any updates that were
pending at the time (waiting in the run loop) fail with the workflow termination and do not
receive a clean rejection. This is identical to the `OnActivateAsync` and `OnTimerAsync` failure
path. The implication: any state preparation in `OnBeforeContinueAsNewAsync` must be complete
before the hook returns; partial work should be validated up-front.

**`OnDeactivateAsync` is different:** exceptions are swallowed and logged. Deactivation must
complete regardless of cleanup failures.

### Activation failure and recovery

After a workflow terminates due to an `OnActivateAsync` failure, the same object ID is usable
again. A new `update-with-start` (or `GetOrCreateAsync`) on the same ID starts a fresh execution
because `WorkflowIdReusePolicy.AllowDuplicate` is set explicitly on all start operations. The
terminated execution's history is not contaminated into the new one.

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

Wire names are post-`Async`-strip (the SDK strips the trailing `"Async"` suffix per
`WorkflowUpdateDefinition.cs`). `"OnReminder"` is the wire name for `OnReminderAsync`.
`"Deactivate"` is **not** in `FrameworkUpdateNames` — it is user-initiated and should go through
your `authorize` predicate like any other update.

### Single-tenant vs multi-tenant threat model

`FrameworkUpdateNames.Contains()` in the `authorize` predicate is safe only in **single-tenant
or mTLS-secured** Temporal deployments where namespace access is already the security boundary
(every client in the namespace is trusted). Wire names are predictable from the library source —
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

The secret must be provisioned to both the delivery worker and the target worker. A built-in
helper is planned for v1.1. The manual approach above is the supported path in v1.

---

## Reminders and Idempotency

> **Registration note:** `ReminderDeliveryActivities` is automatically registered when you call
> `AddDurableObjectWorkflows(...)`. You do **not** need to register it manually on the worker
> builder. Adding a duplicate registration will cause an `InvalidOperationException` at worker
> startup.

Reminders are delivered at-least-once. The `ReminderDeliveryActivities.DeliverReminderAsync`
activity derives a stable `UpdateId` from `ActivityExecutionContext.Current.Info.WorkflowId`
(the dispatcher workflow's ID, unique per tick under `ScheduleOverlapPolicy.Skip`), combined
with the target object ID and reminder name:

```
UpdateId = "{dispatcherWorkflowId}:{targetObjectId}:{reminderName}"
```

The Temporal server deduplicates on `WorkflowUpdateOptions.Id` — a retry of the same activity
in the same dispatcher execution re-issues the same ID and has no effect.

The second argument to `OnReminderAsync` is `ReminderDeliveryContext`, which carries the
`DeliveryId` (the dispatcher workflow's ID). Handlers should store the last-seen `DeliveryId`
per reminder name to detect and skip duplicate deliveries:

```csharp
private readonly HashSet<string> _seenDeliveries = [];

[WorkflowUpdate]
public Task OnReminderAsync(string reminderName, ReminderDeliveryContext context)
{
    if (_seenDeliveries.Contains(context.DeliveryId))
        return Task.CompletedTask; // duplicate — skip

    _seenDeliveries.Add(context.DeliveryId);
    // ... process reminder ...
    return Task.CompletedTask;
}
```

### Known limitation: UpdateId dedup across ContinueAsNew

`UpdateId` deduplication in Temporal is per workflow execution (per run ID). If the target
DurableObject performs a ContinueAsNew between a failed reminder delivery activity and its retry,
the new execution has no record of the prior `UpdateId` — the reminder re-delivers as a fresh
update. This is the expected at-least-once behavior. The `DeliveryId` pattern above is the
correct way to achieve at-most-once processing in the presence of CAN boundaries.

---

## Deactivation Drain Protocol

When `DeactivateAsync()` is called from the outside, the handler sets `_deactivating = true` and
returns immediately. The interceptor then rejects any new update that arrives with:

```
ApplicationFailureException(errorType: "ObjectDeactivating", nonRetryable: true)
```

The caller of a rejected update sees `WorkflowUpdateFailedException`. Updates that were already
in-flight when `_deactivating` was set complete normally before the object closes.

The run loop (not a handler) drains all in-flight handlers using
`Workflow.WaitConditionAsync(() => Workflow.AllHandlersFinished)` — this is safe because the
run path is not itself an in-progress handler. Calling `AllHandlersFinished` from inside a
handler deadlocks permanently (the handler is in `inProgressHandlers`; the wait can never
resolve). This is why `DeactivateAsync` sets the flag and returns rather than draining inline.

---

## Versioning Quick Reference

See [ADR 004](../adr/004-versioning-strategy.md) for the full versioning strategy.

| Change | Safe without patching? |
|--------|----------------------|
| Adding a new `[WorkflowQuery]` handler | Yes |
| Adding a parameter with a default value | Yes (backward compatible) |
| Internal logic changes (no handler rename) | Yes |
| Adding / removing / renaming a `[WorkflowUpdate]` handler reachable by live objects | **No — use `Workflow.Patched`** |
| Changing `OnBeforeContinueAsNewAsync` return shape | **No — use `Workflow.Patched`** |
| Removing a handler that may be targeted by in-flight updates | **Forbidden without migration** |

---

## See Also

For common startup mistakes, exception chain patterns, and environment setup issues, see
[TROUBLESHOOTING.md](TROUBLESHOOTING.md).
