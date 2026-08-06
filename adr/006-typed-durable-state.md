# ADR 006 — Typed State Across Continue-as-New

Status: Accepted
Date: 2026-07-12
Related issue: https://github.com/temporal-community/temporal-dotnet-extensions/issues/4

## Context

`DurableObjectBase` cannot infer which fields form an object's durable state. Existing stateful
objects override `OnBeforeContinueAsNewAsync`, construct an ordered `object?[]`, and maintain a
matching `[WorkflowInit]` constructor and `[WorkflowRun]` signature. Argument-order mistakes can
reset or corrupt logical state after history compaction.

The Temporal .NET SDK requires a `[WorkflowInit]` constructor to be declared on the concrete
workflow and to receive the same arguments as `[WorkflowRun]`; constructor attributes and
constructors are not inherited. A generic base therefore cannot transparently own initialization.

## Decision

Add `DurableObjectBase<TState>` and `DurableObjectSnapshot<TState>`.

- A concrete workflow declares matching `[WorkflowInit]` and `[WorkflowRun]` signatures that accept
  an optional snapshot. Its initializer passes that snapshot and a cold-start value to the base.
- A missing snapshot selects the supplied cold-start value.
- The protected `State` property is restored in the workflow initializer before an update handler
  can execute. Initializing it in the run method is unsafe because Update-with-Start may dispatch
  an update before the run method body initializes state.
- Continue-as-New always emits one typed snapshot containing `State`.
- `PrepareStateForContinueAsNewAsync` permits deterministic normalization or migration before the
  snapshot is recorded.
- State must be non-null. The snapshot envelope distinguishes a missing cold-start argument from
  the default value of a value-type state.

The existing non-generic base and manual hook remain supported.

## Consequences

### Positive

- State carry-forward is strongly typed and no longer depends on ordered argument arrays.
- Reference- and value-type state use the same cold-start semantics.
- Existing DurableObjects remain source-, binary-, serialization-, and replay-compatible.
- A future generator can emit the remaining run-method boilerplate without changing the wire
  shape.

### Negative

- The snapshot becomes part of new workflow histories and must remain serialization-compatible.
- Concrete workflows still declare a short initializer and run method until generation is available.
- Applications remain responsible for compatible evolution of `TState` and its serialized fields.

## Alternatives considered

- **Inherited `[WorkflowInit]` constructor:** rejected because the SDK applies only constructors
  declared on the concrete workflow.
- **Use null/default `TState` as the cold-start marker:** rejected because default is a valid value
  for value-type state and null may be meaningful for nullable models.
- **Reflect over instance fields:** rejected because field selection, schema evolution, and replay
  behavior would be implicit and fragile.
- **Require only reference-type state:** rejected because it unnecessarily excludes valid compact
  value models.

## Compatibility

Existing non-generic objects and their histories are unchanged. Adopting the generic base for an
already-running workflow changes its Continue-as-New argument schema and requires an explicit
migration strategy; it must not be treated as a replay-neutral refactoring. The supported default
is to keep existing live workflow types on the non-generic base and use the generic base for new
types. An in-place migration requires application-specific payload compatibility and replay tests.
