# Epic: Safe-by-Construction Durable Actors for Temporal .NET

Status: Proposed
GitHub epic: [#6](https://github.com/temporal-community/durable-objects-dotnet/issues/6)
Completed by: —

## Outcome

Evolve `TemporalCommunity.DurableObjects` into an opinionated durable-actor model that makes
entity-style Temporal workflows difficult to implement incorrectly. The primary value is correct
activation, concurrency, failure, lifecycle, and state-continuity behavior—not hiding that Temporal
is the underlying runtime.

## Principles

- Preserve access to ordinary Temporal workflows for process-oriented use cases.
- Prefer compile-time guidance over runtime surprises.
- Treat workflow-history compatibility as a release constraint.
- Add abstraction only where it prevents a real error or removes repeated protocol code.
- Add tests and documentation only when they protect or explain meaningful behavior.

## Workstreams

1. [Product positioning](001-product-positioning.md)
2. [Correctness guarantees](002-correctness-guarantees.md)
3. [Strongly typed durable state](003-typed-durable-state.md)
4. [Temporal analyzer platform](004-temporal-analyzer-platform.md)
5. Generated asynchronous DurableObject clients
6. Per-call context, cancellation, headers, and RPC options
7. Object identity, metadata, and visibility conventions
8. Performance, replay, NativeAOT, and scale validation

Detailed plans for workstreams 5–8 should be written only after typed-state and analyzer decisions
clarify their constraints.

## Progress

| Workstream | Plan | GitHub issue | Status | Completed by |
|---|---|---|---|---|
| Product positioning | [001](001-product-positioning.md) | [#2](https://github.com/temporal-community/durable-objects-dotnet/issues/2) | Complete | [6cf07ce](https://github.com/temporal-community/durable-objects-dotnet/commit/6cf07ce) |
| Correctness guarantees | [002](002-correctness-guarantees.md) | [#3](https://github.com/temporal-community/durable-objects-dotnet/issues/3) | Complete | [6cf07ce](https://github.com/temporal-community/durable-objects-dotnet/commit/6cf07ce) |
| Strongly typed durable state | [003](003-typed-durable-state.md) | [#4](https://github.com/temporal-community/durable-objects-dotnet/issues/4) | Complete | [e28fd5a](https://github.com/temporal-community/durable-objects-dotnet/commit/e28fd5a) |
| Temporal analyzer platform | [004](004-temporal-analyzer-platform.md) | [#5](https://github.com/temporal-community/durable-objects-dotnet/issues/5) | Complete | [9ba5580](https://github.com/temporal-community/durable-objects-dotnet/commit/9ba5580), [9044961](https://github.com/temporal-community/durable-objects-dotnet/commit/9044961) |
| Generated asynchronous clients | [005](005-generated-async-clients.md) | [#8](https://github.com/temporal-community/durable-objects-dotnet/issues/8) | Complete | [ff7738e](https://github.com/temporal-community/durable-objects-dotnet/commit/ff7738e) |
| Call context and cancellation | [006](006-durable-object-call-context.md) | [#7](https://github.com/temporal-community/durable-objects-dotnet/issues/7) | Complete | [f980640](https://github.com/temporal-community/durable-objects-dotnet/commit/f980640) |
| Object identity and metadata | [007](007-object-identity-and-visibility.md) | [#9](https://github.com/temporal-community/durable-objects-dotnet/issues/9) | Complete | [a5d841e](https://github.com/temporal-community/durable-objects-dotnet/commit/a5d841e) |
| Performance, NativeAOT, and scale | [008](008-performance-replay-aot-scale.md) | [#10](https://github.com/temporal-community/durable-objects-dotnet/issues/10) | In Progress | — |

## Dependencies

- Positioning and guarantee tests establish the public promise.
- Typed-state design must precede generated workflow boilerplate.
- General analyzer rules must remain independent of the DurableObjects runtime.
- Generated clients should replace `DispatchProxy` only after compatibility and NativeAOT behavior
  are proven.

## Epic completion criteria

- The package clearly states when it should and should not be used.
- Core actor guarantees have black-box integration coverage.
- Declared object state survives Continue-as-New without argument-shape boilerplate.
- Vanilla Temporal .NET code receives high-confidence best-practice diagnostics.
- DurableObjects contracts and generated code receive package-specific diagnostics.
- Typed client calls support asynchronous queries and client-side call options.
- A published NativeAOT sample and replay-compatibility suite pass in CI.
