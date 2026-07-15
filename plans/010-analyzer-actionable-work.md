# Plan 010: Actionable Analyzer Rules and Research Backlog

Status: Complete
Depends on: Plan 009 (Active)
GitHub issue: TBD

## Outcome

Deliver the small set of analyzer improvements that can be specified and tested locally now,
while separating whole-program or SDK-design questions into follow-up GitHub research issues.
Do not publish packages as part of this plan.

Progress: TEMP011 and TEMP013 implementation, tests, documentation, and sample updates are
complete locally. Verification passed, and the deferred research issues are linked below.

## Implementation scope

### Phase 1 — Direct console I/O diagnostic

Add a general-package diagnostic for direct console access inside `[Workflow]` types.

- Use a new catalog ID after confirming the next available general-rule ID.
- Report direct `Console.Write`, `Console.WriteLine`, and direct access to `Console.In`, `Console.Out`,
  and `Console.Error`.
- Categorize it as `Temporal.Observability`, not `Temporal.Determinism`.
- Record confidence level A in the rule catalog because the initial implementation uses exact
  `System.Console` symbol checks.
- Use warning severity unless SDK guidance establishes that the call is always invalid.
- Do not add an automatic code fix until the supported `Workflow.Logger` API and formatting behavior
  are verified against the supported Temporal .NET SDK assets.
- Add a focused catalog entry and update `docs/ANALYZERS.md`.

### Phase 2 — Conservative synchronization diagnostic

Promote TEMP011 from Candidate to Implemented for constructs that can be identified with high
confidence:

- C# `lock` statements.
- `System.Threading.Monitor.Enter`, `TryEnter`, `Exit`, `Wait`, `Pulse`, and `PulseAll`.

`Monitor.TryEnter` is included because it is the non-blocking acquisition overload of the same
thread-synchronization API. Other synchronization types remain deferred until they have their own
false-positive review.

Do not initially flag every synchronization type, concurrent collection, or arbitrary blocking API.
Those require a separate false-positive review. No automatic fix should be offered because the
correct Temporal replacement depends on workflow intent.

### Phase 3 — Consumer verification assets

- Extend the normal `.csproj` analyzer sample with one example for each new diagnostic.
- Keep workflow and non-workflow examples side by side so consumers can verify analyzer boundaries.
- Update the sample README with the package-version limitation: new diagnostics are visible after
  consuming a package containing the implementation; local tests remain the source-level proof until
  the next package release.
- Keep the existing file-based app focused on package smoke verification unless it can consume the
  new package without introducing duplicate coverage.

## Tests

For TEMP013:

- report `Console.Write` and `Console.WriteLine` in a workflow;
- report `Console.In`, `Console.Out`, and `Console.Error` in a workflow;
- do not report the same expressions outside a workflow;
- verify warning severity and diagnostic category.

For TEMP011:

- report `lock` and the supported `Monitor` methods in a workflow;
- do not report them outside a workflow;
- verify unsupported synchronization APIs remain unreported until explicitly added;
- verify no code fix is registered.

Run the analyzer unit tests, code-fix tests, sample build, and full solution build. Do not add tests
that only assert private helper structure or duplicate existing TEMP001–TEMP008 coverage.

## Documentation

- Update `plans/analyzers/rule-catalog.md` only when a rule is actually implemented.
- Update `docs/ANALYZERS.md` with the shipped diagnostic behavior and code-fix availability.
- Update `samples/08-analyzer-project/README.md` and its source when the sample demonstrates the
  new diagnostics.
- Do not add research details to user-facing docs.

## Research issues

The research pass produced the following outcomes:

1. **Transitive workflow nondeterminism analysis** — [#11](https://github.com/temporal-community/temporal-dotnet-extensions/issues/11) (open; prototype required)
   Evaluate Roslyn call-graph analysis, cross-assembly behavior, caching, recursion, generated code,
   and hierarchical diagnostics. Start with a prototype and false-positive benchmark; do not change
   existing lexical rule behavior until the prototype is accepted.

2. **Cryptographic randomness in workflows** — [#12](https://github.com/temporal-community/temporal-dotnet-extensions/issues/12) (implemented in TEMP008; diagnostic-only, no code fix)
   Inventory `RandomNumberGenerator` and related APIs across supported target frameworks, confirm the
   Temporal .NET deterministic alternative, and decide whether this extends TEMP008 or needs a new ID.
   Include an API matrix and code-fix feasibility decision.

3. **Temporal side-effect boundaries** — [#13](https://github.com/temporal-community/temporal-dotnet-extensions/issues/13) (closed; no equivalent API in current SDK)
   Verify the supported .NET `SideEffect`/mutable-side-effect APIs and define how future transitive
   analysis treats callbacks passed to them. Include replay-safety tests and false-positive examples.

4. **Collection iteration determinism** — [#14](https://github.com/temporal-community/temporal-dotnet-extensions/issues/14) (open; symbol-based rule design required)
   Determine which .NET collection/enumeration patterns can change workflow history across replay.
   Compare `Dictionary`, `HashSet`, concurrent collections, and explicitly ordered alternatives before
   proposing a diagnostic.

5. **Workflow discovery beyond `[Workflow]`** — [#15](https://github.com/temporal-community/temporal-dotnet-extensions/issues/15) (closed; current attribute model is authoritative)
   Assess whether registration-based or convention-based workflow discovery is needed in addition to
   the current attribute-based scope, and document the impact on analyzer precision and performance.

## Completion criteria

- TEMP013 and the approved TEMP011 subset have catalog entries, implementation tests, boundary tests,
  and concise user documentation.
- The normal `.csproj` sample contains the new diagnostic triggers and documents that package
  publication is required for consumers to observe them.
- All relevant builds and tests pass.
- Each research topic has a GitHub issue linked from this plan with its current disposition.
- No package is packed, published, or released by this plan.
