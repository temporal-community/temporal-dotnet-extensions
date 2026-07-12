# Plan 003: Strongly Typed Durable State

Status: In Progress
Depends on: Plan 002
GitHub issue: [#4](https://github.com/temporal-community/durable-objects-dotnet/issues/4)
Completed by: —

## Outcome

Let a stateful DurableObject declare one typed state model that the framework carries through
Continue-as-New, eliminating manual `object?[]` construction and constructor-order coupling.

## Current problem

Instance fields survive replay within an execution but not Continue-as-New. Developers must return
ordered constructor arguments from `OnBeforeContinueAsNewAsync` and maintain a matching workflow
initializer. A mismatch can reset or corrupt the logical object's state.

## Design

Add `DurableObjectBase<TState>` with a protected typed `State` and framework-owned carry-forward
through `DurableObjectSnapshot<TState>`. The concrete `[WorkflowInit]` constructor restores state
before Update-with-Start can dispatch a handler; matching initializer and run signatures satisfy
the Temporal SDK contract. The existing base remains supported.

ADR 006 records the design and rejected alternatives.

## Compatibility

- Existing `DurableObjectBase` implementations must continue to compile and replay.
- State payload changes must follow Temporal data-converter compatibility rules.
- Generated or handwritten run signatures must remain compatible with existing histories.
- Existing live workflow types should remain on the non-generic base. Adopting the generic base
  changes the workflow argument schema and is supported for new workflow types or through an
  application-specific migration with replay-compatible payload conversion.

## Implementation

- [x] Prototype viable run and initializer signatures against the Temporal .NET SDK.
- [x] Choose the state ownership and mutation API; record the decision in ADR 006.
- [x] Define old-history and state-schema migration behavior.
- [x] Implement the smallest runtime surface that owns typed carry-forward.
- [x] Defer analyzer support until the runtime contract is stable.

## Tests

- A cold Update-with-Start observes initialized typed state.
- Value-type state survives multiple Continue-as-New boundaries.
- Existing non-generic objects continue to compile and operate in the full suite.

State-model evolution remains governed by the configured Temporal data converter; do not duplicate
serializer tests that do not exercise library behavior.

## Documentation

Update the state and Continue-as-New guidance, one stateful sample, and migration notes. Do not
duplicate general Temporal serialization documentation.

## Completion criteria

- A stateful object does not manually construct Continue-as-New argument arrays.
- Compatibility behavior and adoption boundaries are documented and verified by integration tests.
- Existing objects remain supported without adopting the new history schema.
