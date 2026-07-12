# Plan 002: Correctness Guarantees

Status: Proposed
Depends on: Plan 001 for final terminology
GitHub issue: [#3](https://github.com/temporal-community/durable-objects-dotnet/issues/3)
Completed by: —

## Outcome

Turn the library's differentiating actor semantics into a small, readable black-box test suite that
serves as the release contract.

## Proposed changes

Organize integration coverage around these guarantees:

- concurrent cold updates create one canonical execution;
- updates are serialized across awaits by default;
- an unexpected update exception fails the operation without wedging the object;
- accepted updates finish during deactivation and later updates are rejected;
- explicit state survives worker restart and Continue-as-New;
- authorization rejection prevents handler execution.

Reuse or rename existing scenarios where they already prove the behavior. Add a test only when a
guarantee is currently unproven.

## Implementation

- [ ] Map existing integration scenarios to the guarantees above.
- [ ] Identify missing behavioral boundaries and redundant coverage.
- [ ] Rename or document unclear scenarios without rewriting valid tests.
- [ ] Add the minimum missing black-box tests.
- [ ] Ensure failures report the violated guarantee clearly.

## Tests

This plan is the test workstream. Avoid tests for trivial helpers, framework behavior in isolation,
or multiple syntax variations with the same execution path.

## Documentation

Update README claims only where a guarantee is proven or its exact semantics need clarification.
Do not create a separate test-documentation guide.

## Completion criteria

- Each advertised actor guarantee maps to a passing integration test.
- Tests exercise public APIs and observable Temporal behavior rather than private implementation.
- The suite remains practical to run in CI.
