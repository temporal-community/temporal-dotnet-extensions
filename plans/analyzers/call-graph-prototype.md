# Workflow Call-Graph Prototype

This is a research prototype for issue [#11](https://github.com/temporal-community/temporal-dotnet-extensions/issues/11).
It intentionally lives in `TemporalCommunity.Extensions.Analyzers.Tests` and is not part of the
shipped analyzer assembly.

## What it proves

- Roslyn symbols can identify source methods and same-assembly call edges.
- Existing nondeterminism checks can be propagated from a helper method to a `[Workflow]` caller.
- A fixed-point traversal terminates on recursive helper cycles.
- Safe helpers remain unreported.

The prototype currently recognizes `Task.Delay` and `DateTime.Now`/`UtcNow` as direct findings,
then propagates those findings through same-compilation method calls.

## Test matrix

The prototype tests contain three cases:

| Case | Expected result |
|---|---|
| Workflow calls helper that reads `DateTime.UtcNow` | One propagated finding |
| Workflow calls helper that returns a constant | No finding |
| Workflow calls a recursive helper cycle with no direct violation | No finding and termination |

This is a precision smoke test, not a performance benchmark. It deliberately avoids timing-based
assertions that would be unstable in CI.

## Findings and limitations

- Same-compilation propagation is feasible with ordinary Roslyn `SemanticModel` and symbol maps.
- Cross-assembly propagation requires analyzer facts or another persisted summary format; this
  prototype does not attempt it.
- The prototype does not yet model delegates, virtual/interface dispatch, overrides, generated code,
  conditional compilation, or external library summaries.
- It reports one propagated reason per method and does not yet render a full hierarchical call chain.
- The implementation duplicates a minimal direct-rule predicate rather than calling private
  production analyzer helpers; production extraction should happen only after the design is accepted.

The cross-assembly prototype test emits a helper assembly, exports a method summary keyed by its
fully qualified symbol name, and consumes that summary while analyzing a separate workflow
compilation. This is a stand-in for persisted analyzer facts and confirms that symbol identity can
be the summary key. It does not yet implement Roslyn's analyzer-fact serialization protocol.

## Recommendation

Do not enable transitive diagnostics in the production analyzer yet. The next research increment
should replace the stand-in summary with Roslyn analyzer facts across two test assemblies, measure
analysis latency on a representative solution, and define dispatch/override and generated-code
policies. Promote only after false-positive and performance thresholds are agreed.
