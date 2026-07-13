# Plan 004: Temporal Analyzer Platform

Status: In Progress
Depends on: Initial rule research; Plan 003 for typed-state-specific rules
GitHub issue: [#5](https://github.com/temporal-community/durable-objects-dotnet/issues/5)
GitHub epic: [#1](https://github.com/temporal-community/durable-objects-dotnet/issues/1)
Completed by: —

## Outcome

Provide high-confidence compile-time guidance for vanilla Temporal .NET applications and stronger
DurableObjects-specific diagnostics and generation without coupling the general rules to this
runtime.

## Projects and packages

- `TemporalCommunity.Extensions.Analyzers`: general Temporal .NET analyzers and code fixes.
- `TemporalCommunity.DurableObjects.Analyzers`: DurableObjects analyzers, code fixes, and generators.

The DurableObjects package may reuse the general analyzer infrastructure. The general package must
not reference `TemporalCommunity.DurableObjects`.

## Rule sources

Build the candidate rule catalog from:

1. Temporal .NET SDK behavior and source;
2. official Temporal documentation and samples;
3. vetted Temporal agent skills;
4. this library's tested policies and failure modes.

Every implemented rule must cite its sources and distinguish an SDK correctness requirement from
an opinionated recommendation. Time-sensitive guidance must record the SDK or documentation version
reviewed.

## Proposed phases

1. [x] Inventory and classify candidate rules by severity, confidence, and analysis cost.
2. [x] Specify the first small set of precise general Temporal diagnostics.
3. [x] Establish Roslyn testing and rule-documentation infrastructure.
4. [x] Implement the first general analyzer rules (`TEMP001`–`TEMP003`).
5. [x] Add the first DurableObjects contract rules (`DO0001`–`DO0004`).
6. [x] Add separate code-fix assemblies and package them with their related analyzers.
7. [ ] Add generation only after Plans 005 and 006 approve the client and call-context APIs.

ADR 007 records the accepted package structure: one user-facing package contains separate
compiler-safe analyzer/generator and Workspace-dependent code-fix assemblies.

Initial high-confidence candidates include workflow `ConfigureAwait(false)`, known nondeterministic
APIs in workflow context, `Task.Delay` in workflows, invalid workflow/query signatures, and missing
required activity timeouts. Each candidate still requires SDK-source verification before approval.

## Tests

For each rule, test the diagnostic case, the important non-diagnostic boundary, location/severity,
and code-fix output when a fix exists. Add inheritance, partial-type, or data-flow cases only when
they exercise a credible ambiguity. Generator tests should verify generated public shape and compile
the output; snapshots alone are insufficient for behavioral guarantees.

## Documentation

- Maintain concise user-facing rule pages for shipped diagnostics, including rationale, examples,
  suppression guidance, and authoritative sources.
- Document package installation and configuration once.
- Keep research notes and unshipped rule specifications under `plans/`, not `docs/`.

The current catalog is `plans/analyzers/rule-catalog.md`; shipped-rule guidance is consolidated in
`docs/ANALYZERS.md` to avoid low-value per-rule pages for the initial set.

## Completion criteria

- The two packages have clear, independent ownership boundaries.
- Every shipped rule is source-backed, low-noise, documented, and meaningfully tested.
- General rules work in a vanilla Temporal .NET project with no DurableObjects reference.
- DurableObjects rules activate only when their symbols are present.
- Generators are introduced only for stable public contracts.
