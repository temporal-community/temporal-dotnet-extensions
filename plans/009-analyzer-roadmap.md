# Plan 009: Analyzer and Code-Fix Roadmap

Status: Active
Depends on: Plan 004 (Complete); current analyzer and code-fix baseline
GitHub issue: TBD

Progress: Phase 1 diagnostics TEMP004, TEMP007, and TEMP008 are implemented and covered by
focused analyzer tests. Mechanical fixes are provided for Task.Run, Thread.Sleep, and supported
random/GUID replacement cases; Task.Wait and timeout-based cancellation diagnostics remain
diagnostic-only where an automatic rewrite would require application-specific intent.

## Outcome

Expand the analyzer and code-fix packages with a small, high-signal set of Temporal .NET rules
drawn from the Temporal developer skill, the SDK documentation, and this repository's DurableObjects
constraints.

The goal is not to cover every Temporal best practice. It is to add compile-time guidance where the
compiler can make misuse obvious, the fix is mechanical, and false positives can stay low.

## Package split

- `TemporalCommunity.Extensions.Analyzers`: vanilla Temporal .NET workflow rules.
- `TemporalCommunity.DurableObjects.Analyzers`: DurableObjects-specific rules and generator support.

Keep the general package independent of `TemporalCommunity.DurableObjects`.

## Catalog alignment

This roadmap extends the existing rule catalog rather than minting duplicate IDs.

- `TEMP004`, `TEMP007`, and `TEMP008` are already cataloged as candidate rules and are the first
  promotion targets for the general analyzer package.
- `TEMP005` and `TEMP006` remain research items; they should only move into implementation after
  we confirm the overload-sensitive analysis and code-fix shape.
- `DO0005` is already implemented in the generator and should be treated as shipped behavior, not
  new roadmap work.
- `TEMP011` and `TEMP013` are cataloged and implemented as focused synchronization and console
  I/O diagnostics.

## Follow-on candidates (after Phase 1)

- `TEMP005`: `Task.WhenAny` overloads with unsafe semantics.
- `TEMP006`: `Task.WhenAll` in workflow types.

## Suggested roadmap

### Phase 1: Promote existing workflow determinism rules

Promote the cataloged candidate rules to implemented diagnostics without changing their IDs:

- `TEMP004`: `Task.Run` -> `Workflow.RunTaskAsync`
- `TEMP007`: `Thread.Sleep`, `Task.Wait`, and timeout-based `CancellationTokenSource` usage ->
  `Workflow.DelayAsync` or the appropriate deterministic alternative
- `TEMP008`: `Guid.NewGuid` and non-workflow random APIs -> `Workflow.NewGuid` / `Workflow.Random`

These are all strong fits for code fixes because the safer Temporal alternative is explicit and
mechanical.

### Phase 2: Workflow shape and boundary rules

Add diagnostics for patterns that are legal C# but violate Temporal workflow rules or common
workflow best practices:

- query handlers must remain read-only;
- update validators must remain read-only and non-blocking;
- workflow methods should prefer Temporal abstractions over raw .NET scheduling helpers;

Keep this phase conservative. If a rule cannot be checked with low noise, leave it out for now.

### Phase 3: DurableObjects-specific contract guidance

Add package-specific diagnostics that enforce the library's supported object model:

- `DeactivateAsync` overrides must retain `[WorkflowUpdate]`;
- durable-object contracts should continue to reject unsupported handler kinds and async query
  shapes that the runtime cannot support.

`DO0005` already covers generated-client eligibility for unsupported shapes. Keep that behavior
stable, but do not treat it as new roadmap work here.

This package should stay focused on DurableObjects conventions that are unique to the library rather
than duplicating general Temporal workflow guidance.

## Suggested diagnostic buckets

Use buckets to keep the rules understandable and easy to suppress when needed:

- `Temporal.Determinism` for workflow replay safety;
- `Temporal.WorkflowShape` for query/update validator boundaries;
- `Temporal.DurableObjects` for object-model rules and generated-client eligibility;
- `Temporal.Observability` for direct logging and console-I/O guidance that is not itself a
  determinism violation.

## Tests

For each new diagnostic:

- test the positive case;
- test the non-diagnostic boundary that proves the rule is not overreaching;
- test the code fix when one exists;
- add one focused integration or generated-output test only when the rule affects public shape.

Avoid adding tests that merely re-state the implementation.

## Documentation

Update only the user-facing docs that describe shipped rules:

- `docs/ANALYZERS.md` for the diagnostic catalog and install guidance;
- `README.md` only for package-level entry-point changes;
- sample or troubleshooting docs only when a shipped diagnostic changes the recommended code shape.

Keep design research and unshipped rule notes in `plans/`, not `docs/`.

## Non-goals

- Replay-testing guidance
- Versioning and patching policy
- Activity idempotency guidance
- Observability guidance beyond the focused TEMP013 console I/O rule
- Payload-size guidance
- Child workflow, saga, and schedule design guidance
- Generated-client eligibility redesign or new contract-shape work; that belongs in the generated
  client plans unless it is required to preserve shipped `DO0005` behavior.

Those topics matter, but they are better handled by docs and runtime guidance than by analyzer
diagnostics.

## Completion criteria

- Phase 1 rules are implemented with low-noise code fixes.
- Phase 2 rules are added only where the analyzer can prove the violation reliably.
- DurableObjects rules remain separated from the general package.
- Tests cover each shipped diagnostic and fix.
- `docs/ANALYZERS.md` matches the shipped rule set.
