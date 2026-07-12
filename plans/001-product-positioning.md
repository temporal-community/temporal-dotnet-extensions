# Plan 001: Product Positioning

Status: Complete
Depends on: None
GitHub issue: [#2](https://github.com/temporal-community/durable-objects-dotnet/issues/2)
Completed by: [6cf07ce](https://github.com/temporal-community/durable-objects-dotnet/commit/6cf07ce)

## Outcome

Describe `TemporalCommunity.DurableObjects` as a durable-actor model with safe defaults, while
making clear that Temporal supplies durability and that ordinary workflows remain the better fit
for process-oriented orchestration.

## Proposed changes

- Rewrite the README introduction around atomic activation, serialized updates, contained update
  failures, and managed lifecycle behavior.
- Add concise “use DurableObjects / use a workflow” guidance.
- State important boundaries: explicit Continue-as-New state carry-forward, no signals, synchronous
  typed queries, namespace-wide workflow IDs, and current NativeAOT limitations.
- Correct inaccurate resource, configuration, and lifecycle claims in user documentation.
- Align the NuGet package description with the same positioning.

## Implementation

- [x] Inventory current claims in the README, guides, samples, and package metadata.
- [x] Rewrite the top-level value proposition and decision guidance.
- [x] Correct or remove claims not supported by implementation or tests.
- [x] Check links and terminology across user-facing material.

## Tests

No new runtime tests are required. Build and package verification should confirm documentation and
metadata changes do not break packing or samples.

## Documentation

This workstream is documentation-focused. Prefer improving existing entry points over adding new
pages.

## Completion criteria

- A Temporal .NET developer can tell within the README whether the abstraction fits their use case.
- Every advertised correctness guarantee is implemented and covered by an existing or planned test.
- Package metadata and user documentation use consistent language.
