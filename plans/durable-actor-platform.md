# Epic: Safe-by-Construction Durable Actors for Temporal .NET

Status: Proposed
GitHub epic: To be created
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
| Product positioning | [001](001-product-positioning.md) | To be created | Proposed | — |
| Correctness guarantees | [002](002-correctness-guarantees.md) | To be created | Proposed | — |
| Strongly typed durable state | [003](003-typed-durable-state.md) | To be created | Proposed | — |
| Temporal analyzer platform | [004](004-temporal-analyzer-platform.md) | To be created | Proposed | — |
| Generated asynchronous clients | Not yet planned | — | Deferred | — |
| Call context and cancellation | Not yet planned | — | Deferred | — |
| Object identity and metadata | Not yet planned | — | Deferred | — |
| Performance, NativeAOT, and scale | Not yet planned | — | Deferred | — |

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
