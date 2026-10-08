# Temporal .NET Analyzers

Two opt-in analyzer packages provide compile-time guidance and IDE code fixes:

- Use `TemporalCommunity.Extensions.Analyzers` for replay-safety and handler-shape checks in any
  Temporal .NET workflow project.
- Add `TemporalCommunity.DurableObjects.Analyzers` for DurableObjects contract checks and generated
  clients. DurableObjects projects can use both packages.

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

The general package does not depend on the DurableObjects runtime.

```bash
dotnet add package TemporalCommunity.DurableObjects.Analyzers
```

See the [project sample](../samples/08-analyzer-project/) for common diagnostics and fixes, or
the [.NET 10 file-based app](../samples/07-analyzer-file-app/) for a no-project-file example.
The file-based sample is pinned to `0.3.2`; the catalog below describes this source tree.

## General Temporal rules

| ID | Meaning | Code fix |
|---|---|---|
| `TEMP001` | `ConfigureAwait(false)` leaves Temporal's workflow task scheduler. | Remove `ConfigureAwait(false)`. |
| `TEMP002` | `Task.Delay` uses a system timer and is not replay-safe. | Replace it with `Workflow.DelayAsync`. |
| `TEMP003` | `DateTime` or `DateTimeOffset` system-clock reads are not replay-safe. | Replace it with `Workflow.UtcNow`. |
| `TEMP004` | `Task.Run` uses the system task scheduler and is not replay-safe. | Replace it with `Workflow.RunTaskAsync`. |
| `TEMP005` | Higher-risk generic `Task.WhenAny<TResult>` overloads may not preserve workflow scheduler compatibility on older target frameworks. | Replace the flagged call with `Workflow.WhenAnyAsync`; two-result and non-generic forms are not flagged. |
| `TEMP007` | `Thread.Sleep`, `Task.Wait`, and timeout-based cancellation sources use system timing. | `Thread.Sleep` becomes `await Workflow.DelayAsync(...)`; choose the appropriate pattern for `Task.Wait` and timeout-based cancellation sources. |
| `TEMP008` | System, cryptographic random, and GUID APIs are not replay-safe. | Replace them with `Workflow.NewGuid` or `Workflow.Random` where the fix is applicable; cryptographic APIs are diagnostic-only because security semantics differ. |
| `TEMP009` | Inline activity options omit both required timeout properties. | None; choose an application-specific `StartToCloseTimeout` or `ScheduleToCloseTimeout`. Variables and factory-created options are intentionally not analyzed. |
| `TEMP010` | `CancellationTokenSource.CancelAsync` is not supported in workflow code. | Replace a standalone call with `CancellationTokenSource.Cancel`; expression-context calls remain diagnostic-only. |
| `TEMP011` | `lock`, selected `Monitor` calls, and `System.Threading.Semaphore`, `SemaphoreSlim`, or `Mutex` use unsupported thread synchronization in workflow code. | None; use `Temporalio.Workflows.Semaphore` or `Mutex` where workflow-local coordination is appropriate. |
| `TEMP013` | Direct `Console` I/O bypasses Temporal workflow logging. | None; use `Workflow.Logger` with the supported Temporal logging API. |
| `TEMP014` | `Dictionary`, `HashSet`, and `ConcurrentDictionary` enumeration order is not guaranteed across replay. | None; use an ordered collection or sort values before iterating. |
| `TEMP015` | `[WorkflowQuery]` methods cannot return task-like types (`Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>`). | None; queries must compute and return a value synchronously. |
| `TEMP016` | `[WorkflowUpdateValidator]` methods must return `void`. | None; validator failures should be reported by throwing an application-specific exception. |
| `TEMP017` | A validator's parameter count and parameter types must match its associated `[WorkflowUpdate]` method. | None; change the validator signature to match the update contract. |
| `TEMP018` | A validator must name an existing `[WorkflowUpdate]` method on the same workflow type. | None; use the update method's CLR name in `WorkflowUpdateValidatorAttribute`. |
| `TEMP019` | Each workflow update may have at most one validator. | None; choose the single validator that owns the update validation policy. |

These diagnostics apply to code lexically contained in a type with `[Workflow]`. They do not
claim to inspect arbitrary external helper libraries; workflow replay tests remain necessary.

Update validators are synchronous and read-only. They validate the proposed arguments and reject
an update by throwing; they must not issue workflow commands:

```csharp
[WorkflowUpdate]
public Task<int> AddAsync(int value) => Task.FromResult(value);

[WorkflowUpdateValidator(nameof(AddAsync))]
public void ValidateAdd(int value)
{
    if (value < 0)
        throw new ArgumentOutOfRangeException(nameof(value));
}
```

## DurableObjects rules

| ID | Meaning | Code fix |
|---|---|---|
| `DO0001` | A contract method is not a Task-returning update or synchronous query. | Add the appropriate update or query attribute. |
| `DO0002` | A DurableObject declares a signal, which the programming model does not support. | Replace the signal attribute with an update attribute. |
| `DO0003` | A concrete DurableObject is missing its declared `[WorkflowRun]` method. | None; the correct run signature depends on state shape. |
| `DO0004` | A typed-state object does not declare matching optional snapshot initializer and run signatures. | None; initializer construction requires an application state decision. |
| `DO0005` | A contract uses a shape that cannot produce a generated client. | None; make the contract public and non-generic, and avoid generic/ref/dynamic handlers or generated-name collisions. |
| `DO0007` | A `DurableObjectBase` override of `DeactivateAsync` does not retain `[WorkflowUpdate]`. | Add `[WorkflowUpdate]` back to the override (reuses the same code fix that adds a handler attribute for `DO0001`). |

## Generated DurableObject clients

`TemporalCommunity.DurableObjects.Analyzers` also generates one concrete client per supported
contract. A contract such as `ICounter` produces `CounterDurableObjectClient` and a
`GetCounterClient` factory extension. Synchronous contract queries remain available for source
compatibility, while the concrete client adds asynchronous query methods and call-options
overloads. A module initializer registers the concrete implementation so ordinary `Get<ICounter>`
calls avoid `DispatchProxy` too.

Generation currently requires a public, top-level, non-generic contract. Existing applications
that do not install the analyzer package continue to use the runtime proxy fallback.

Suppress a rule only after establishing that the reported code cannot execute in workflow context.
Project-wide suppression of determinism rules is not recommended.
