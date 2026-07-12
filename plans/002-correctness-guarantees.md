# Plan 002: Correctness Guarantees

Status: Complete
Depends on: Plan 001 for final terminology
GitHub issue: [#3](https://github.com/temporal-community/durable-objects-dotnet/issues/3)
Completed by: [6cf07ce](https://github.com/temporal-community/durable-objects-dotnet/commit/6cf07ce)

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

## Guarantee coverage

| Guarantee | Integration coverage |
|---|---|
| Concurrent cold updates create one canonical execution | `ScenarioC_ConcurrencySingleActivation` |
| Updates serialize across awaits by default | `ScenarioN_WorkerInterceptorInvariants` |
| An unexpected update failure does not wedge the object | `ScenarioN_WorkerInterceptorInvariants` |
| Deactivation finishes accepted updates and rejects later updates | `ScenarioN_WorkerInterceptorInvariants` |
| Explicit state survives worker restart and Continue-as-New | `ScenarioD_DurabilityWorkerFailure` and `ScenarioB_ContinueAsNewStateSurvival` |
| Authorization rejection prevents handler execution | `ScenarioN_WorkerInterceptorInvariants` |

## Implementation

- [x] Map existing integration scenarios to the guarantees above.
- [x] Identify missing behavioral boundaries and redundant coverage.
- [x] Rename or document unclear scenarios without rewriting valid tests.
- [x] Add the minimum missing black-box tests.
- [x] Ensure failures report the violated guarantee clearly.

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
