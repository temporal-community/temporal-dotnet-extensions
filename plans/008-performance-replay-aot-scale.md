# Plan 008: Performance, Replay, NativeAOT, and Scale Validation

Status: In Progress
Depends on: Plans 003 and 005
GitHub issue: [#10](https://github.com/temporal-community/durable-objects-dotnet/issues/10)
Completed by: —

## Outcome

Protect the durable-object execution model with repeatable replay, dispatch-performance,
NativeAOT, and bounded scale verification. These checks are regression signals, not production
capacity claims.

## Implementation

- Add a committed workflow-history fixture and replay it with the current worker implementation.
- Add BenchmarkDotNet cases comparing generated-client and compatibility-proxy construction and
  dispatch overhead without imposing machine-dependent pass/fail thresholds.
- Keep the published generated-client NativeAOT smoke test in the required CI matrix.
- Add a bounded integration scenario that activates, updates, queries, and deactivates many
  independent object IDs concurrently.
- Run replay and NativeAOT checks in CI; keep benchmarks opt-in and publish their usage.

## Tests and documentation

Use one representative typed-state history that crosses Continue-as-New. The scale scenario must
assert isolation and completion, not elapsed time. Document how maintainers refresh replay
fixtures and run benchmarks; avoid user-facing performance promises without controlled evidence.

## Completion criteria

- A checked-in history is replayed in CI against the current DurableObject workflow code.
- The generated-client registry is executed by a published NativeAOT binary in CI.
- Benchmarks provide generated-client and proxy baselines.
- A concurrent multi-object integration scenario proves state isolation at bounded scale.
- The full test, package-consumer, and CI matrix passes.
