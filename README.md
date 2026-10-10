# TemporalCommunity.Extensions

Community-built extensions and compile-time guardrails for the
[Temporal .NET SDK](https://github.com/temporalio/sdk-dotnet).


## Which offering fits?

| If you want to... | Start here |
|---|---|
| Create a plain Temporal .NET worker or a Worker + Client + Shared solution | [Generate and run a template solution](docs/getting-started.md#start-with-templates) |
| Add replay-safety checks to ordinary Temporal workflows | [Inspect the general analyzer examples](docs/getting-started.md#start-with-general-analyzers) |
| Model a long-lived entity with a stable ID and serialized updates | [Run the Durable Objects page-counter demo](docs/getting-started.md#start-with-durable-objects) |

## Packages

| Package | Purpose |
|---|---|
| [`TemporalCommunity.Templates`](https://www.nuget.org/packages/TemporalCommunity.Templates) | `dotnet new` templates for Temporal workflows, activities, workers, and Worker + Client + Shared solutions, with optional Aspire and OpenTelemetry. |
| [`TemporalCommunity.Extensions.Analyzers`](https://www.nuget.org/packages/TemporalCommunity.Extensions.Analyzers) | Replay-safety analyzers and code fixes for ordinary Temporal .NET workflows. |
| [`TemporalCommunity.DurableObjects`](https://www.nuget.org/packages/TemporalCommunity.DurableObjects) | An opinionated durable-actor model for long-lived entities addressed by stable ID. |
| [`TemporalCommunity.DurableObjects.Analyzers`](https://www.nuget.org/packages/TemporalCommunity.DurableObjects.Analyzers) | Durable Objects contract analyzers, code fixes, and generated asynchronous clients. |


## Temporal workflow analyzers

Add compile-time checks to any Temporal .NET workflow project:

```bash
dotnet add package TemporalCommunity.Extensions.Analyzers
```

The package detects replay-safety and workflow-shape misuse in ordinary Temporal workflows, with
IDE code fixes where a deterministic replacement is safe. These diagnostics also work in projects
that do not reference the Durable Objects runtime.

See [Temporal .NET Analyzers](docs/analyzers.md) for installation details, the full rule catalog,
code-fix behavior, limitations, and Durable Objects generator requirements.

## Durable Objects

`TemporalCommunity.DurableObjects` is for entity-style workflows such as accounts, devices,
sessions, etc. It adds start-on-first-update through Update-with-Start, serialized updates,
contained update failures, and typed state across Continue-as-New.

Use a plain Temporal workflow when the execution represents a process with a defined end or needs
signals, child workflows, orchestration-heavy control flow, or unrestricted SDK behavior. See
[Durable Objects concepts](docs/durable-objects.md) for the detailed comparison and lifecycle model.

### Install

```bash
dotnet add package TemporalCommunity.DurableObjects
dotnet add package TemporalCommunity.Extensions.Analyzers
dotnet add package TemporalCommunity.DurableObjects.Analyzers
```

> Requires Temporal Server v1.28.0 or later for Update-with-Start. The runtime targets .NET 10,
> .NET 8, and .NET Standard 2.1. .NET Standard does not support visibility-listing APIs.

### Minimal example

This is a conceptual sketch of the Durable Objects programming model, not a complete runnable
application. For the full worker and caller setup, run [sample 01](samples/01-getting-started/).

Define a contract and implementation:

```csharp
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

[Workflow]
public interface ICounter : IDurableObject
{
    [WorkflowUpdate] Task<int> IncrementAsync(int amount);
    [WorkflowQuery] int GetCount();
}

[Workflow]
public sealed class Counter : DurableObjectBase<int>, ICounter
{
    [WorkflowInit]
    public Counter(DurableObjectSnapshot<int>? snapshot = null) : base(snapshot, 0) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<int>? snapshot = null) => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task<int> IncrementAsync(int amount)
    {
        State += amount;
        return Task.FromResult(State);
    }

    [WorkflowQuery]
    public int GetCount() => State;
}
```

This example calls Durable Objects and runs the worker in the same process, so it registers both
sides.
- `AddDurableObjects` provides the client-side `IDurableObjectFactory` and its default task
queue.
- `AddDurableObjectWorkflows` registers the object workflows and Durable Objects runtime on the
worker.

If callers and workers run in separate services, each service registers only the side it
uses; their task-queue names must match.

```csharp
services.AddTemporalClient(options => options.TargetHost = "localhost:7233");
// Caller side: register the factory used to get Durable Object clients.
services.AddDurableObjects("my-task-queue");

// Worker side: register object workflows and their runtime behavior.
services.AddHostedTemporalWorker("my-task-queue")
    .AddDurableObjectWorkflows(typeof(Counter).Assembly);
```

Use the concrete client generated by `TemporalCommunity.DurableObjects.Analyzers`:

```csharp
var counter = factory.GetCounterClient("counter-42");
await counter.IncrementAsync(5);
var current = await counter.GetCountAsync();
```

`IncrementAsync(5)` is a workflow update. If `counter-42` is not running, Update-with-Start starts
the object and submits the increment in one operation. Its state is rebuilt from Temporal history
after worker restarts and carried through Continue-as-New by `DurableObjectBase<TState>`.

Start with the runnable [getting-started sample](samples/01-getting-started/) and the
[Durable Objects starting path](docs/getting-started.md#start-with-durable-objects) for validators, activities, lifecycle behavior,
failure handling, and production guidance.

## Samples

Six runnable Durable Objects samples cover generated clients, validation, scheduling and reminders,
object-to-object calls through activities, observability, and testing. See the
[samples index](samples/README.md) or the [repository-wide getting-started paths](docs/getting-started.md).

For the Durable Objects demos, start a local Temporal server and run sample 01:

```bash
temporal server start-dev
just run-sample
```

## Documentation

Start with [Getting started](docs/getting-started.md) to choose a path for templates, analyzers,
or Durable Objects.

### Templates and analyzers

- [Temporal .NET templates](docs/templates.md): Generate workflows, activities, converters,
  workers, or solutions with `dotnet new`.
- [Temporal .NET analyzers](docs/analyzers.md): Install replay-safety and Durable Objects checks,
  explore code fixes, and use generated Durable Objects clients.

### Durable Objects

- [Concepts and API](docs/durable-objects.md): Decide when to use Durable Objects and learn their
  identity, state, updates, scheduling, and API.
- [Lifecycle and deactivation](docs/tier-model.md): Understand when an object stays resident, when
  it closes, and how history is compacted. This guide calls those choices "tiers."
- [Failure handling](docs/failure-handling.md): Handle client and lifecycle failures,
  authorization, and reminder retries.
- [Troubleshooting](docs/troubleshooting.md): Diagnose common worker, query, scheduling, and
  configuration problems.

## Building from source

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) and
[`just`](https://github.com/casey/just).

```bash
dotnet tool restore
just build
just test
```

Run `just` to list all recipes. Samples 01–05 use an external Temporal server (by default at
`localhost:7233`); sample 06 starts an SDK-managed local server; analyzer samples 07–08 need no
server. Full package verification (`just pack-verify`) and the local
CI-equivalent (`just ci`) use Unix/Bash tooling and are supported on Linux and macOS. On Windows,
after `dotnet tool restore`, run `just test-unit` from PowerShell 7 (`pwsh`); that recipe uses
PowerShell-safe single-line `dotnet test` commands. GitHub Actions repeats the build and tests on
Windows and runs package verification on Ubuntu.

## Contributing

1. Fork and clone the repository.
2. Run `dotnet tool restore` once.
3. Before opening a pull request, run `just ci` on Linux/macOS or `just test-unit` on Windows.
4. Add or update meaningful tests and user documentation when behavior changes.
5. Document current behavior and compatibility constraints in the relevant guide under `docs/`.

## License

MIT — see [LICENSE](LICENSE).
