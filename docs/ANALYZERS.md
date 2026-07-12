# Temporal .NET Analyzers

Two opt-in analyzer packages provide compile-time guidance:

- `TemporalCommunity.Extensions.Analyzers` applies to vanilla Temporal .NET workflows.
- `TemporalCommunity.DurableObjects.Analyzers` adds DurableObjects contract checks.

Install the package appropriate for the project that declares workflows:

```xml
<PackageReference Include="TemporalCommunity.Extensions.Analyzers" Version="...">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

DurableObjects projects may reference both packages. The general package does not depend on the
DurableObjects runtime.

## General Temporal rules

| ID | Meaning | Supported replacement |
|---|---|---|
| `TEMP001` | `ConfigureAwait(false)` leaves Temporal's workflow task scheduler. | Remove it, or use `ConfigureAwait(true)` when explicit configuration is necessary. |
| `TEMP002` | `Task.Delay` uses a system timer and is not replay-safe. | `Workflow.DelayAsync` |
| `TEMP003` | `DateTime` or `DateTimeOffset` system-clock reads are not replay-safe. | `Workflow.UtcNow` |

These diagnostics apply to code lexically contained in a type with `[Workflow]`. They do not
claim to inspect arbitrary external helper libraries; workflow replay tests remain necessary.

## DurableObjects rules

| ID | Meaning |
|---|---|
| `DO0001` | A contract method is not a Task-returning update or synchronous query. |
| `DO0002` | A DurableObject declares a signal, which the programming model does not support. |
| `DO0003` | A concrete DurableObject is missing its declared `[WorkflowRun]` method. |
| `DO0004` | A typed-state object does not declare matching optional snapshot initializer and run signatures. |

Suppress a rule only after establishing that the reported code cannot execute in workflow context.
Project-wide suppression of determinism rules is not recommended.

