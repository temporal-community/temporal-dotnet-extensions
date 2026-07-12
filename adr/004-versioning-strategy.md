# ADR 004 — Versioning Strategy for Long-Lived DurableObjects

**Status:** ACCEPTED

---

## Context

DurableObjects are Temporal workflow executions. A live object has accumulated event history
that the Temporal server can replay at any time — worker restart, sticky-cache eviction, or
task timeout. Replay re-executes the workflow code against the recorded history. If the code
has changed in a way that produces different commands for the same history, the SDK raises a
non-determinism error and the object stops processing updates.

Unlike short-lived workflows that complete in seconds, DurableObjects may run for months or
years. Any code change that affects what Temporal commands are recorded for a given input is a
breaking change for live objects.

The library cannot abstract `Workflow.Patched` — it lives in user workflow code and requires
explicit developer judgment. This ADR makes the rules explicit so the decision is not left to
guesswork.

---

## Decision

The library provides:
1. This ADR documenting what is safe vs. forbidden.
2. `DurableObjectWorkerOptions` that integrates with `WorkerDeploymentOptions` for side-by-side
   versioned deployments.
3. A reference to `WorkflowReplayer` as the pre-release validation tool.

The library does **not** auto-detect breaking changes. That requires human judgment.

---

## What Changes Are Safe Without Patching

These changes do not alter the sequence of Temporal commands recorded in the history. Live
objects replay correctly after a rolling worker deploy.

| Change | Why it's safe |
|--------|--------------|
| Adding a new `[WorkflowQuery]` handler | Queries are not recorded in workflow history; they execute against live state on demand. |
| Adding a parameter with a default value to an existing handler | Callers not supplying the parameter produce the same recording as before. |
| Adding a parameter to a `[WorkflowQuery]` method | Same reasoning — queries are not recorded. |
| Changing internal logic that does not alter what Temporal commands are issued | Pure in-memory computation changes are invisible to the history. |
| Changing log messages, metrics emission, or non-Temporal I/O | These are side effects outside the workflow event log. |

---

## What Requires `Workflow.Patched`

`Workflow.Patched("patch-id")` records a marker in the workflow history. New code that checks
the marker will branch: live objects that ran before the patch return `false`; new objects or
objects that have replayed past the patch marker return `true`. This allows the same binary to
handle both old-format history and new-format history.

| Change | Why it requires patching |
|--------|--------------------------|
| Adding a `[WorkflowUpdate]` handler reachable by live objects | A live object that has not yet replayed past the new handler's first invocation will encounter an unknown command and non-determinism. |
| Removing a `[WorkflowUpdate]` handler that may appear in live history | The worker no longer has a registered handler for an update the history expects. SDK error on replay. |
| Renaming a `[WorkflowUpdate]` handler without an explicit wire-name pin | The old wire name disappears from the worker; any history that recorded the old name fails replay. |
| Changing `OnBeforeContinueAsNewAsync` return shape (CAN constructor schema change) | The prior run's CAN arguments are recorded in history; the new run's `[WorkflowInit]` constructor must accept both the old and new arg shapes. |

### Renaming a handler safely

Use `[WorkflowUpdate("old-name")]` to pin the wire name while renaming the C# method:

```csharp
// Before rename — wire name is "Increment" (Async-stripped)
[WorkflowUpdate]
public Task IncrementAsync() { ... }

// After rename — C# method is "AddAsync" but wire name is still "Increment"
[WorkflowUpdate("Increment")]
public Task AddAsync() { ... }
```

Live objects continue to receive updates on wire name `"Increment"`. Callers that use the typed
proxy still call `AddAsync()` on the C# interface — the proxy resolves the wire name from the
`[WorkflowUpdate]` attribute.

### Patch ID naming convention

Patch IDs are global within a workflow type's history. Collisions across teams or features
produce incorrect branching. Use the convention:

```
"{HandlerName}-{change-description}-{YYYYMM}"
```

Examples:
- `counter-add-max-value-202506` — added `MaxValue` parameter to `Counter` in June 2025
- `order-remove-legacy-status-202509` — removed `LegacyStatus` field from `Order` in Sept 2025
- `session-rename-update-to-refresh-202601` — renamed `UpdateAsync` to `RefreshAsync`

Keep the description lowercase-kebab and specific enough to be unique across the codebase. Avoid
generic names like `counter-fix-202506` — the "fix" label tells future engineers nothing about
the branching intent.

### CAN constructor schema versioning

When `[WorkflowInit]` constructor parameters change, the prior run's `OnBeforeContinueAsNewAsync`
produces the old arg shape. Use `Workflow.Patched` to discriminate:

```csharp
// Old constructor (before schema change)
[WorkflowInit]
public Counter(int initialCount) { _count = initialCount; }

// New constructor (after schema change — adds a label)
[WorkflowInit]
public Counter(int initialCount, string label = "")
{
    _count = initialCount;
    // Patched guard: old executions won't have a label in their CAN args
    _label = Workflow.Patched("add-label-field") ? label : string.Empty;
}
```

Old executions that CAN without the new arg will pass `string.Empty` for `label` (default).
New executions record the patch marker and use the provided label on the next CAN.

---

## Worker Deployment Strategy

### Using `WorkerDeploymentOptions` (preferred)

Side-by-side versioned worker deployment lets you roll out new code without forcing live objects
onto the new binary before they replay safely. Use `WorkerDeploymentOptions`:

```csharp
var version = new WorkerDeploymentVersion("my-app", "2.0.0");
var deployment = new WorkerDeploymentOptions(version, useWorkerVersioning: true)
{
    DefaultVersioningBehavior = VersioningBehavior.AutoUpgrade,
};

services.AddHostedTemporalWorker("my-task-queue", deployment)
        .AddDurableObjectWorkflows(typeof(Counter).Assembly);
```

`WorkerDeploymentVersion` is a `sealed record(string DeploymentName, string BuildId)`.
`VersioningBehavior` is the enum `Temporalio.Common.VersioningBehavior` with values
`Unspecified`, `Pinned`, and `AutoUpgrade`.

Objects that carry `[Workflow(VersioningBehavior = VersioningBehavior.Pinned)]` at the class
level are pinned to the worker version that created them. They will not auto-upgrade to a new
deployment until you explicitly migrate them. Use `Pinned` when the object has complex history
that you want to validate with replay tests before moving it to the new binary.

**Do not use `TemporalWorkerOptions.BuildId`.** It is `[Obsolete("Use DeploymentOptions instead")]`.

### Deployment procedure

1. Deploy the new worker version alongside the old (side-by-side). Both poll the same task queue.
2. New workflow executions are dispatched to the new version.
3. Live objects on `AutoUpgrade` migrate to the new version when their task is next scheduled.
4. Once all `Pinned` executions have been manually migrated or replayed successfully, retire the
   old worker version.
5. Never kill a worker with open workflow executions — allow it to drain naturally.

---

## Forbidden Changes Without Migration

These changes will break live objects without a migration plan and cannot be fixed by
`Workflow.Patched` alone:

| Forbidden change | What breaks |
|-----------------|-------------|
| Removing a `[WorkflowUpdate]` handler that may be targeted by in-flight updates | History records the update; worker has no handler; non-determinism error. |
| Renaming a handler without `[WorkflowUpdate("old-name")]` pin | Wire name disappears; same failure as removal. |
| Changing a `[WorkflowQuery]` to return a different type without changing the name | Callers get a deserialization error; the change is invisible to history but breaks the wire contract. |
| Removing `[WorkflowRun]` from `RunAsync()` | Worker cannot find the entry point; object stops processing. |

---

## Replay Tests

Before each release, run `WorkflowReplayer` with captured event histories to assert that the
new binary replays correctly against real production histories:

```csharp
var replayer = new WorkflowReplayer(
    new WorkflowReplayerOptions().AddWorkflow<Counter>());

await replayer.ReplayWorkflowAsync(
    await WorkflowHistory.FromJsonAsync(capturedHistoryJson));
```

Capture history from a running workflow using the Temporal CLI before deploying the new binary:

```
temporal workflow show --workflow-id <id> --output json > testdata/workflow-history.json
```

Commit the captured JSON into the repository under `testdata/` alongside the replay test. If
replay fails, apply `Workflow.Patched` before deploying. Add one replay test per scenario before
each release.

---

## Rationale

The library cannot make versioning transparent — it is a property of the Temporal execution
model, not of the DurableObject abstraction. Attempting to hide it would produce invisible
correctness bugs when the abstraction leaked. Making the rules explicit through this ADR gives
developers a clear decision framework instead of leaving them to discover non-determinism errors
in production.

---

## Consequences

- Developers must read this ADR before renaming, adding, or removing update handlers on a type
  with live workflow executions.
- Replay tests are a required part of the pre-release checklist, not optional.
- `BuildId` is not used; `DeploymentOptions` is the versioning path.
- v1.1 may introduce a static analyzer that warns when a handler rename lacks a wire-name pin.
