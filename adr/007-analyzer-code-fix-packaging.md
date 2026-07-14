# ADR 007 — Analyzer and Code-Fix Packaging

Status: Accepted
Date: 2026-07-12
Related plan: `plans/004-temporal-analyzer-platform.md`
Related issue: https://github.com/temporal-community/temporal-dotnet-extensions/issues/5

## Context

Diagnostic analyzers run in command-line compiler hosts and should depend only on Roslyn compiler
APIs. Code fixes edit documents through Roslyn Workspaces. Referencing Workspaces from the analyzer
assembly violates Roslyn rule `RS1038` and can make command-line loading unpredictable.

Established projects including xUnit and Roslynator separate analyzer and code-fix projects while
shipping related assemblies in one user-facing NuGet package.

## Decision

Each analyzer product ships one NuGet package containing two assemblies:

- a compiler-safe analyzer/generator assembly; and
- a code-fix assembly that references the analyzer and Roslyn Workspaces.

The package IDs remain `TemporalCommunity.Extensions.Analyzers` and
`TemporalCommunity.DurableObjects.Analyzers`. Code-fix assemblies are implementation details and
do not create additional packages for consumers to select.

Source generators use compiler APIs and may live in the corresponding analyzer assembly. They must
not depend on Workspaces.

## Consequences

- `dotnet build` can load diagnostics without Workspace dependencies.
- IDE hosts can discover fixes from the companion assembly.
- Analyzer and code-fix tests remain separate.
- Packaging tests must assert both assemblies are present in each NuGet package.
- Generated clients remain deferred until their public API and call-context design are approved.
