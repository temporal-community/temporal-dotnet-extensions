# Temporal .NET Analyzer Rule Catalog

Reviewed against Temporal .NET SDK 1.16.0 on 2026-07-12.

## Authorities

- Temporal .NET SDK README, especially Workflow Logic Constraints and .NET Task Determinism:
  https://github.com/temporalio/sdk-dotnet#workflow-logic-constraints
- Temporal .NET SDK API documentation shipped in the 1.16.0 NuGet package.
- Official Temporal .NET samples and SDK repository examples:
  https://github.com/temporalio/samples-dotnet
- DurableObjects policies backed by this repository's runtime and integration tests.

The local Temporal developer skill at `/Users/cecilphillip/Dev/workspace/skill-temporal-developer/`
was reviewed after this catalog was first drafted. Its .NET determinism, testing, error-handling,
and observability references are now treated as supplemental guidance alongside the SDK sources.

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
| TEMP004 | Do not use `Task.Run` in workflow types | Error | A | Implemented |
| TEMP005 | Prefer `Workflow.WhenAnyAsync` over higher-risk generic `Task.WhenAny` overloads | Warning | B | Implemented |
| TEMP006 | Prefer `Workflow.WhenAllAsync` over `Task.WhenAll` | Info | B | Research |
| TEMP007 | Do not use `Thread.Sleep`, `Task.Wait`, or timeout-based `CancellationTokenSource` | Error | B | Implemented |
| TEMP008 | Do not use non-workflow, cryptographic random, or GUID APIs | Error | B | Implemented |
| TEMP009 | Inline activity options require `StartToCloseTimeout` or `ScheduleToCloseTimeout` | Error | A* | Implemented |
| TEMP010 | Do not use `CancellationTokenSource.CancelAsync` in workflows | Error | A | Implemented |
| TEMP011 | Do not use thread synchronization primitives in workflows | Error | B | Implemented |
| TEMP013 | Do not write directly to the console in workflows | Warning | A | Implemented |
| TEMP014 | Do not iterate over unordered collections in workflows | Error | B | Implemented |
| TEMP015 | Workflow query methods must not return task-like types | Error | A | Implemented |
| TEMP012 | Suppress incompatible platform diagnostics for workflow source | Info | C | Research |

The first implementation recognizes code lexically contained in a type carrying
`Temporalio.Workflows.WorkflowAttribute`. It intentionally does not claim to find unsafe calls
hidden in arbitrary external helper libraries. Call-graph analysis is a later, separately evaluated
capability.

\* TEMP009 is high-confidence only for inline `ActivityOptions` and `LocalActivityOptions` object
creations passed directly to workflow activity calls. Variables, factories, and other data-flow
patterns are intentionally left unreported.

## DurableObjects rules

| ID | Rule | Severity | Confidence | Status |
|---|---|---:|---:|---|
| DO0001 | DurableObject contract methods must use the supported update/query shapes | Error | A | Implemented |
| DO0002 | DurableObjects must not declare workflow signals | Error | A | Implemented |
| DO0003 | Concrete DurableObjects require a valid `[WorkflowRun]` declaration | Error | A | Implemented |
| DO0004 | Generic typed-state initializer and run snapshot signatures must match | Error | A | Implemented |
| DO0005 | A contract uses a shape that cannot produce a generated client | Error | A | Implemented |
| DO0006 | A live non-generic object cannot adopt typed snapshot state without migration | Warning | D | Documentation/replay |

## Deferred guidance

Arbitrary I/O, external mutable state, nondeterministic collection iteration, activity idempotency,
and workflow-history compatibility cannot be diagnosed reliably with a simple local analyzer. They
remain documentation and replay-test concerns until a low-noise analysis design is demonstrated.
