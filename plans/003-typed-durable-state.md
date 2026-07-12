# Plan 003: Strongly Typed Durable State

Status: Proposed
Depends on: Plan 002
GitHub issue: To be created
Completed by: —

## Outcome

Let a stateful DurableObject declare one typed state model that the framework carries through
Continue-as-New, eliminating manual `object?[]` construction and constructor-order coupling.

## Current problem

Instance fields survive replay within an execution but not Continue-as-New. Developers must return
ordered constructor arguments from `OnBeforeContinueAsNewAsync` and maintain a matching workflow
initializer. A mismatch can reset or corrupt the logical object's state.

## Proposed direction

Evaluate a generic base API such as `DurableObjectBase<TState>` with a protected typed `State` and
framework-owned carry-forward behavior. Keep the existing base class for stateless objects and
source compatibility.

The design must define initialization, serialization, schema evolution, workflow run signatures,
and migration of histories created by the current API before implementation begins.

## Compatibility

- Existing `DurableObjectBase` implementations must continue to compile and replay.
- State payload changes must follow Temporal data-converter compatibility rules.
- Generated or handwritten run signatures must remain compatible with existing histories.
- A migration path is required before deprecating manual carry-forward hooks.

## Implementation

- [ ] Prototype viable run and initializer signatures against the Temporal .NET SDK.
- [ ] Choose the state ownership and mutation API; record the decision in an ADR.
- [ ] Define old-history and state-schema migration behavior.
- [ ] Implement the smallest runtime surface that owns typed carry-forward.
- [ ] Add analyzer support only after the runtime contract is stable.

## Tests

- State survives multiple Continue-as-New boundaries.
- Existing non-generic objects continue to replay and operate.
- State serialization failure has an explicit, useful failure mode.
- A representative compatible state-model evolution replays successfully.

Avoid testing record properties or Temporal serialization independently of library behavior.

## Documentation

Update the state and Continue-as-New guidance, one stateful sample, and migration notes. Do not
duplicate general Temporal serialization documentation.

## Completion criteria

- A stateful object does not manually construct Continue-as-New argument arrays.
- Compatibility behavior is documented and verified by replay-oriented tests.
- Existing objects have a supported incremental migration path.
