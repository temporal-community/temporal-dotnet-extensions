# Temporal .NET Analyzers

Two opt-in analyzer packages provide compile-time guidance and IDE code fixes:

- `TemporalCommunity.Extensions.Analyzers` applies to vanilla Temporal .NET workflows.
- `TemporalCommunity.DurableObjects.Analyzers` adds DurableObjects contract checks.

Both packages include their IDE code fixes. There are no separate code-fix packages to install.

Install the package appropriate for the project that declares workflows:

```bash
dotnet add package TemporalCommunity.Extensions.Analyzers
```

```xml
<PackageReference Include="TemporalCommunity.Extensions.Analyzers" Version="...">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

DurableObjects projects may reference both packages. The general package does not depend on the
DurableObjects runtime.

```bash
dotnet add package TemporalCommunity.DurableObjects.Analyzers
```

## General Temporal rules

| ID | Meaning | Code fix |
|---|---|---|
| `TEMP001` | `ConfigureAwait(false)` leaves Temporal's workflow task scheduler. | Remove `ConfigureAwait(false)`. |
| `TEMP002` | `Task.Delay` uses a system timer and is not replay-safe. | Replace it with `Workflow.DelayAsync`. |
| `TEMP003` | `DateTime` or `DateTimeOffset` system-clock reads are not replay-safe. | Replace it with `Workflow.UtcNow`. |

These diagnostics apply to code lexically contained in a type with `[Workflow]`. They do not
claim to inspect arbitrary external helper libraries; workflow replay tests remain necessary.

## DurableObjects rules

| ID | Meaning | Code fix |
|---|---|---|
| `DO0001` | A contract method is not a Task-returning update or synchronous query. | Add the appropriate update or query attribute. |
| `DO0002` | A DurableObject declares a signal, which the programming model does not support. | Replace the signal attribute with an update attribute. |
| `DO0003` | A concrete DurableObject is missing its declared `[WorkflowRun]` method. | None; the correct run signature depends on state shape. |
| `DO0004` | A typed-state object does not declare matching optional snapshot initializer and run signatures. | None; initializer construction requires an application state decision. |
| `DO0005` | A contract uses a shape that cannot produce a generated client. | None; make the contract public and non-generic, and avoid generic/ref/dynamic handlers or generated-name collisions. |

## Generated DurableObject clients

`TemporalCommunity.DurableObjects.Analyzers` also generates one concrete client per supported
contract. A contract such as `ICounter` produces `CounterDurableObjectClient` and a
`GetCounterClient` factory extension. Synchronous contract queries remain available for source
compatibility, while the concrete client adds asynchronous query methods and call-options
overloads. A module initializer registers the concrete implementation so ordinary `Get<ICounter>`
calls avoid `DispatchProxy` too.

Generation currently requires a public, top-level, non-generic contract. Existing applications
that do not install the analyzer package continue to use the runtime proxy fallback.

Each NuGet package contains a compiler-safe analyzer assembly and a separate Workspace-dependent
code-fix assembly. Consumers still install only the one package shown above.

Suppress a rule only after establishing that the reported code cannot execute in workflow context.
Project-wide suppression of determinism rules is not recommended.
