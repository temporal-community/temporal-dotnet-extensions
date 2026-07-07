# ADR 003 — DurableObject-to-DurableObject Messaging Deferred to v1.1

**Status:** ACCEPTED

---

## Context

DurableObjects frequently need to call other DurableObjects — a session object updating a user
profile object, an order object notifying an inventory object. The PoC had no canonical pattern
for this. Three design candidates were identified during v1 planning.

---

## Decision

DurableObject-to-DurableObject messaging is deferred from v1. No helper API ships in this
release. The v1 workaround is an Activity that calls the target object directly via
`IDurableObjectFactory` or `ITemporalClient`.

---

## Rationale

**Nexus investigation required.** Temporal Nexus provides typed RPC services backed by
workflows and enables cross-namespace calls. The correct long-term abstraction for DO→DO
messaging should align with how Nexus exposes workflow-to-workflow communication — committing
to a non-Nexus API surface before that investigation concludes would create a design that needs
to be replaced or extended in v1.1 anyway.

**A premature helper could anchor the wrong pattern.** Activity-mediated calls, child workflows,
and Nexus each have different trade-offs for latency, failure isolation, and cross-namespace
support. Shipping a convenience `DOCallActivity<T>` without resolving those trade-offs would
lock callers into Activity-mediated semantics before the team can evaluate whether Nexus is the
better fit.

---

## v1 Workaround

Use an Activity that calls the target object. Activities run outside the workflow scheduler and
may use `ITemporalClient` or `IDurableObjectFactory` freely.

```csharp
// Activity — runs outside the workflow context; may use async I/O
[Activity]
public class DurableObjectCallActivity(IDurableObjectFactory factory)
{
    public async Task<int> GetRemoteCountAsync(string targetObjectId)
    {
        var counter = factory.Get<ICounter>(targetObjectId);
        return counter.GetCount();
    }

    public async Task IncrementRemoteAsync(string targetObjectId)
    {
        var counter = factory.Get<ICounter>(targetObjectId);
        await counter.IncrementAsync();
    }
}

// Inside an update handler — call the activity
[WorkflowUpdate]
public async Task SyncCountAsync(string remoteId)
{
    _count = await Workflow.ExecuteActivityAsync(
        (DurableObjectCallActivity a) => a.GetRemoteCountAsync(remoteId),
        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) })
        .ConfigureAwait(true);
}
```

This pattern is correct and production-safe. The activity provides retry semantics, failure
isolation between the calling and target objects, and a clear point for adding authentication
headers if needed.

---

## Three Candidate Designs for v1.1

| Approach | Description | Trade-offs |
|----------|-------------|------------|
| Activity-mediated | `Workflow.ExecuteActivityAsync` calls the target via `IDurableObjectFactory`. Ships as a convenience helper wrapping the pattern above. | Existing, proven. Adds one activity round-trip per call. No cross-namespace. |
| Child workflows | The calling object spawns a child workflow whose sole purpose is delivering an update to the target. | More workflow overhead. Handles long-lived calls naturally. |
| Nexus | A Nexus operation handler dispatches the call as a typed RPC. Cross-namespace capable. | Requires Nexus API stabilization in the .NET SDK and server. Most future-proof. |

The decision among these three is deferred until the Nexus investigation completes. The v1.1
scope includes: prototype each approach, benchmark latency, evaluate cross-namespace behavior,
and ship the winning abstraction as a library extension.

---

## Consequences

- v1 callers who need DO→DO calls must write the Activity + factory pattern themselves. This is
  documented in the README under "Object-to-Object Communication."
- No `DOCallActivity<T>` or equivalent helper ships in v1. Introducing one without the Nexus
  investigation would anchor the wrong abstraction.
- v1.1 will ship a first-class helper after the design decision is made.
