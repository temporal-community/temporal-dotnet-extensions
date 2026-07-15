# Analyzer project sample

This is a conventional SDK-style .NET 10 project that consumes
`TemporalCommunity.Extensions.Analyzers` `0.3.2`. Use it when you want VS Code or another IDE to
load the analyzer and code-fix assemblies through a normal project workspace.

Open this directory as a folder in VS Code with the C# extension/C# Dev Kit enabled, then restore
the project. The workflow intentionally contains `TEMP001`, `TEMP002`, `TEMP003`, `TEMP004`,
`TEMP007`, `TEMP008`, `TEMP011`, `TEMP013`, and `TEMP014` violations. Build diagnostics should appear in the
editor, and the
lightbulb actions should offer fixes for `ConfigureAwait(false)`, `Task.Delay`, system-clock
reads, `Task.Run`, `Guid.NewGuid`, and supported `Random` replacements.

TEMP011 and TEMP013 are diagnostics without automatic code fixes: replace synchronization with
Temporal coordination and route logging through `Workflow.Logger`.

TEMP014 is also diagnostic-only: use an ordered collection or sort values before iterating.

The sample is expected to fail its build until the violations are fixed. Apply the code fixes in
the editor, or replace the workflow body with Temporal-safe APIs before running it.

```bash
dotnet restore
dotnet build
```
