# ADR 005 — `[WorkflowSignal]` Banned in v1

**Status:** ACCEPTED

---

## Context

The original PoC used `[WorkflowSignal]` for `DeactivateAsync` and potentially for other
fire-and-forget notifications. During the v1 planning review, the failure modes of signal
handlers were analyzed against the DurableObject programming model.

The specific trigger: `DeactivateAsync` as a signal means the caller has no confirmation that
deactivation was accepted. If the object is in a bad state, the signal is silently dropped.
More fundamentally, the authorization hook (`Func<HandleUpdateInput, bool>`) on
`DurableObjectWorkerInterceptor` is scoped to updates — signal handlers bypass it entirely.

---

## Decision

All `[WorkflowSignal]` usage is banned in v1 — on every DurableObject interface and
implementation, including `IDurableObject.DeactivateAsync`. `DeactivateAsync` is
`[WorkflowUpdate]`.

`AddDurableObjectWorkflows` enforces this at worker startup: any concrete class carrying a
`[WorkflowSignal]` method throws `InvalidOperationException` at registration time with a clear
message pointing to this ADR.

`ValidateInterface<T>()` enforces this at `factory.Get<T>()` time: any interface method carrying
`[WorkflowSignal]` throws `InvalidOperationException` at proxy creation time.

There are no signal exemptions. Including `DeactivateAsync` — it is `[WorkflowUpdate]`.

---

## Rationale

Three independent reasons. All three must be resolved before reconsideration.

### 1. Signals bypass the authorization hook

`DurableObjectWorkerInterceptor.HandleUpdateAsync` is the interception point for authorization.
The hook type is `Func<HandleUpdateInput, bool>` — update-scoped by design. Signal handlers
are invoked via `HandleSignalAsync`, which the interceptor does not override. A signal bypasses
every authorization check the application registers. There is no way to add signal-level auth
without a second interceptor hook with a different input type — a significant API addition that
has no analog in the update model.

### 2. No caller confirmation

Signals are fire-and-forget. The caller gets no acknowledgment that the signal was received,
processed, or rejected. For `DeactivateAsync`, this means:

- The caller cannot know whether the object actually deactivated.
- If the object was in a wedged state (crashed workflow task), the signal is queued but never
  processed. The caller has no way to detect this.
- The caller cannot implement a timeout or retry with confirmation.

As a `[WorkflowUpdate]`, `DeactivateAsync` returns only after the handler runs. The caller
knows the deactivation request was accepted. A timeout is trivially set via `UpdateOptions`.

### 3. No rollback on partial state mutation

Signal handlers run to completion or fail. There is no rollback mechanism for in-memory state
mutations made before a throw. For `[WorkflowSignal]` specifically:

- If the handler throws an `ApplicationFailureException` (or any other
  `Temporalio.Exceptions.FailureException` subclass — `ApplicationFailureException` is the one
  callers typically construct directly),
  the SDK calls `FailWorkflowExecution` — the **workflow terminates**. Any state mutations made
  before the throw are lost; the object is gone.
- If the handler throws any other exception, the SDK retries the workflow task indefinitely.
  The object wedges permanently. Any state mutations made before the throw may be repeated or
  lost depending on when the retry fires.

Neither path is acceptable for a stateful actor. An update handler's failure modes are:
`ApplicationFailureException` → clean rejection (object stays alive); arbitrary exception →
interceptor safety net wraps it as `ApplicationFailureException` → clean rejection (object stays
alive). The update model has a safe fallback. The signal model does not.

---

## `DeactivateAsync` as Update Is Strictly Better

Comparing `[WorkflowSignal]` vs `[WorkflowUpdate]` for `DeactivateAsync`:

| Property | Signal | Update |
|----------|--------|--------|
| Caller confirmation | None | Yes — `await DeactivateAsync()` resolves when accepted |
| Authorization hook | Bypassed | Runs — same `authorize` predicate as all updates |
| Drain protocol (non-deadlocking) | Deadlocks if handler awaits `AllHandlersFinished` | Non-deadlocking — drain runs from the run path |
| Rejection on wedged object | Silent drop | `WorkflowUpdateFailedException` with clear error |

The drain protocol deserves elaboration. `DeactivateAsync` must not `await Workflow.AllHandlersFinished`
from inside itself because it is an in-progress handler — `inProgressHandlers.Count` includes
itself and can never reach zero from that context. This is a permanent deadlock.

As an update, the handler sets `_deactivating = true` and returns immediately. The run loop
(which is not an in-progress handler) then monitors `_deactivating` and safely awaits
`Workflow.AllHandlersFinished`. This design is non-deadlocking by construction.

If `DeactivateAsync` were a signal, the drain could only be implemented by polling a condition
from the signal handler itself — which would also deadlock in the same way. There is no correct
drain implementation for a signal-based deactivation.

---

## Impact

**High-throughput fire-and-forget patterns** that previously used signals now require updates.
Each update has a round-trip to the caller. For use cases where the caller does not need
confirmation and the latency of an extra round-trip is unacceptable, this is a real cost.

**Update round-trip latency:** a `[WorkflowUpdate]` round-trip is typically **10–100 ms** on a
local Temporal server (loopback, no load). On a remote cluster the range is wider, driven by
network latency and worker polling interval. A `[WorkflowSignal]` delivers faster because it
does not wait for the handler to run — but provides no confirmation. For the vast majority of
DurableObject use cases, 10–100 ms per call is acceptable and the confirmation semantics are
worth the cost. If profiling shows update latency as a genuine bottleneck on a specific hot
path, that is the evidence needed to revisit this decision in v1.1 (see reconsideration criteria
below).

For v1, the correct response is: if you need fire-and-forget without confirmation, model the
operation as an activity that calls the object and discards the result rather than using a
signal. This is explicit and testable.

---

## v1.1 Reconsideration Criteria

`[WorkflowSignal]` may be reconsidered in v1.1 **only if all three conditions are met:**

1. **Profiling shows update round-trip latency is a measurable bottleneck** for specific
   fire-and-forget use cases where the caller genuinely does not need confirmation (e.g., a
   high-frequency telemetry ingest path).

2. **An authorization hook for signal handlers** is added to `DurableObjectWorkerInterceptor`
   (`HandleSignalAsync`) with the same predicate type as the update hook — so signal-level auth
   is enforced by the same mechanism as update-level auth.

3. **Documentation and tooling** make the no-rollback, no-confirmation semantics of signals
   explicit at the API level (attribute, XML doc) so developers must opt in knowingly.

If any of these conditions is unmet, the ban remains. The bar is high because the update model
is strictly safer and the gap is measurable latency on a hot path — not a design preference.
