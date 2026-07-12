# Temporal .NET Analyzer Rule Catalog

Reviewed against Temporal .NET SDK 1.16.0 on 2026-07-12.

## Authorities

- Temporal .NET SDK README, especially Workflow Logic Constraints and .NET Task Determinism:
  https://github.com/temporalio/sdk-dotnet#workflow-logic-constraints
- Temporal .NET SDK API documentation shipped in the 1.16.0 NuGet package.
- Official Temporal .NET samples and SDK repository examples:
  https://github.com/temporalio/samples-dotnet
- DurableObjects policies backed by this repository's runtime and integration tests.

No Temporal-specific agent skill was installed or present in the workspace during this review.
Skill guidance must be evaluated and added as an authority if such a vetted skill becomes available.

## Confidence levels

| Level | Meaning |
|---|---|
| A | Precise symbol or constant check with a clear supported replacement. |
| B | Local flow or overload-sensitive analysis with documented exceptions. |
| C | Compilation-wide or call-graph analysis with material false-positive risk. |
| D | Better enforced through replay tests, runtime validation, or documentation. |

## Initial general rules

| ID | Rule | Severity | Confidence | Status |
|---|---|---:|---:|---|
| TEMP001 | Do not use `ConfigureAwait(false)` in workflow types | Error | A | Implemented |
| TEMP002 | Do not use `Task.Delay` in workflow types | Error | A | Implemented |
| TEMP003 | Do not access `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, or `DateTimeOffset.UtcNow` in workflow types | Error | A | Implemented |
| TEMP004 | Do not use `Task.Run` in workflow types | Error | A | Candidate |
| TEMP005 | Prefer `Workflow.WhenAnyAsync` over unsafe `Task.WhenAny` overloads | Warning | B | Research |
| TEMP006 | Prefer `Workflow.WhenAllAsync` over `Task.WhenAll` | Info | B | Research |
| TEMP007 | Do not use `Thread.Sleep`, `Task.Wait`, or timeout-based `CancellationTokenSource` | Error | B | Candidate |
| TEMP008 | Do not use non-workflow random or GUID APIs | Error | B | Candidate |
| TEMP009 | Activity options require `StartToCloseTimeout` or `ScheduleToCloseTimeout` | Error | B | Candidate |
| TEMP010 | Do not use `CancellationTokenSource.CancelAsync` in workflows | Error | A | Candidate |
| TEMP011 | Do not use thread synchronization primitives in workflows | Error | B | Candidate |
| TEMP012 | Suppress incompatible platform diagnostics for workflow source | Info | C | Research |

The first implementation recognizes code lexically contained in a type carrying
`Temporalio.Workflows.WorkflowAttribute`. It intentionally does not claim to find unsafe calls
hidden in arbitrary external helper libraries. Call-graph analysis is a later, separately evaluated
capability.

## DurableObjects rules

| ID | Rule | Severity | Confidence | Status |
|---|---|---:|---:|---|
| DO0001 | DurableObject contract methods must use the supported update/query shapes | Error | A | Implemented |
| DO0002 | DurableObjects must not declare workflow signals | Error | A | Implemented |
| DO0003 | Concrete DurableObjects require a valid `[WorkflowRun]` declaration | Error | A | Implemented |
| DO0004 | Generic typed-state initializer and run snapshot signatures must match | Error | A | Implemented |
| DO0005 | A scheduled one-shot object must self-deactivate | Warning | C | Research |
| DO0006 | A live non-generic object cannot adopt typed snapshot state without migration | Warning | D | Documentation/replay |

## Deferred guidance

Arbitrary I/O, external mutable state, nondeterministic collection iteration, activity idempotency,
and workflow-history compatibility cannot be diagnosed reliably with a simple local analyzer. They
remain documentation and replay-test concerns until a low-noise analysis design is demonstrated.
