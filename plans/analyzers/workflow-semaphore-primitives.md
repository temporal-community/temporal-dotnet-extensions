# Workflow semaphore and mutex primitives

Status: Complete

Tracking: Plan 009, Phase 2; extends the completed TEMP011 subset in Plan 010

GitHub issue: TBD

## Outcome

Extend the existing `TEMP011` workflow-synchronization diagnostic to identify the .NET semaphore
and mutex primitives that the Temporal .NET SDK explicitly disallows in workflow code. Do not add a
new diagnostic ID or an automatic code fix.

## Evidence

The Temporal .NET SDK's [task-determinism guidance](https://github.com/temporalio/sdk-dotnet#net-task-determinism)
explicitly says not to use:

- `System.Threading.Semaphore`;
- `System.Threading.SemaphoreSlim`; or
- `System.Threading.Mutex`.

It directs workflow authors to `Temporalio.Workflows.Semaphore` and `Temporalio.Workflows.Mutex`.
The SDK notes that the narrow `SemaphoreSlim.WaitAsync`-without-timeout plus `Release` pattern can
work technically, but still recommends the workflow primitive because other usage can deadlock and
`SemaphoreSlim` requires disposal.

`Temporalio.Workflows.Semaphore` is a deterministic, workflow-only primitive. It has a fixed
maximum equal to its initial count, uses workflow waits and timers, and rejects construction outside
workflow execution. It is not a process-wide or cross-workflow synchronization mechanism.

## Implementation scope

Keep `TEMP011` as an error in the `Temporal.Determinism` category. Extend its existing exact-symbol
checks only within types carrying `[Workflow]`:

- report construction of `System.Threading.Semaphore`, `System.Threading.SemaphoreSlim`, and
  `System.Threading.Mutex`;
- report invocation on receivers statically typed as those exact types, including inherited
  members such as `WaitOne`, so use of a semaphore or mutex supplied from elsewhere is still
  diagnosed;
- continue reporting the existing `lock` and selected `System.Threading.Monitor` constructs;
- do not report `Temporalio.Workflows.Semaphore` or `Temporalio.Workflows.Mutex`.

Use semantic symbols, not spelling, so aliases and fully qualified names behave identically.
Intentionally report the documented narrow `SemaphoreSlim.WaitAsync()`-without-timeout plus
`Release()` pattern too: the rule follows the SDK's recommended workflow primitive and does not
attempt fragile flow analysis to prove that a semaphore stays within that exception.
Do not expand this change to `WaitHandle`, events, `ReaderWriterLockSlim`, concurrent collections,
or third-party synchronization types; each needs its own false-positive and replacement review.

## Code-fix decision

Do not add a code fix.

`SemaphoreSlim` has constructors, disposal requirements, release overloads, and timeout behavior
that do not always map to the Temporal API. `System.Threading.Semaphore` also has OS/named-semaphore
semantics and `WaitOne` APIs. Replacing either can change maximum-count or coordination scope, and
the correct workflow design may instead be activity work, state-machine coordination, a Temporal
semaphore, or a mutex.

The general code-fix provider must explicitly continue to offer no action for `TEMP011`.

## Tests

Add focused analyzer tests for:

- construction and member calls for all three BCL types inside a `[Workflow]` type;
- aliases and fully qualified references resolving to the same BCL symbols;
- the existing `lock` and `Monitor` coverage remaining intact;
- the documented narrow `SemaphoreSlim.WaitAsync()`-without-timeout plus `Release()` pattern still
  reporting, which records the intentional policy boundary;
- `Temporalio.Workflows.Semaphore` and `Temporalio.Workflows.Mutex` remaining silent;
- the same BCL primitives outside a workflow type remaining silent.

Keep tests source-level and deterministic; do not start a worker or rely on actual semaphore
blocking. Keep the existing code-fix test that confirms `TEMP011` has no registered action; no new
assertion is needed unless the provider test surface changes.

## Documentation and samples

- Update `docs/ANALYZERS.md` to name the supported `TEMP011` semaphore and mutex coverage and
  recommend the Temporal equivalents.
- Update `plans/analyzers/rule-catalog.md` to clarify the implemented TEMP011 scope without
  increasing its broad confidence rating.
- Add one concise semaphore trigger to the normal analyzer project sample only if the package
  version used by that sample contains this rule; otherwise update it with the next analyzer
  package version rather than presenting an unobservable trigger.
- Keep the file-based app focused on its existing package smoke coverage.

## Completion criteria

- `TEMP011` covers the three exact BCL semaphore/mutex types plus its existing `lock`/`Monitor`
  coverage without false positives for Temporal primitives or non-workflow code.
- No code fix is offered, with a regression test proving that boundary.
- User-facing analyzer documentation describes the replacement and no-fix decision.
- The relevant analyzer and code-fix tests and the solution build pass.
- No package is published as part of this work.
