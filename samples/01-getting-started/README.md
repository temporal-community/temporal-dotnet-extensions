# Sample 01 — Getting Started

This sample introduces the core DurableObjects programming model through a `PageCounter` object
that tracks page views per URL slug.

## Why DurableObjects?

Persistent state in distributed systems usually means a database row + a background worker +
a polling loop. DurableObjects replace that pattern with a single addressable object that retains
its state as long as it's needed, recovers after worker crashes, and records operations in Temporal
workflow history. If you've used Orleans grains or
Akka.NET actors, DurableObjects are conceptually similar but backed by Temporal's durable
execution engine instead of an in-process scheduler.

## What you'll learn

- How to define a DurableObject contract (`IPageCounter : IDurableObject`)
- How to implement a stateful DurableObject (`PageCounter : DurableObjectBase<PageCounterState>`)
- The required `[WorkflowRun]` boilerplate on the concrete class
- How to call activities from workflow code (`DurableObjectBase.ExecuteActivityAsync`)
- How to wire everything up with `Microsoft.Extensions.Hosting`
- How to obtain a source-generated client via `IDurableObjectFactory`
- How to enumerate canonical objects with rich visibility metadata

## Prerequisites

- [Temporal server running locally](https://docs.temporal.io/cli#start-dev-server): `temporal server start-dev`
- .NET 10 SDK

> **If something goes wrong:** If Temporal isn't running you'll see
> `Grpc.Core.RpcException: Status(StatusCode="Unavailable", ...)`. Run
> `temporal server start-dev` and restart the sample.

## Running

```bash
cd samples/01-getting-started
dotnet run
```

## Key patterns

### Generated client vs. GetOrCreateAsync

```csharp
// No RPC — creates a concrete generated client with async queries.
var client = factory.GetPageCounterClient("home");

// Atomically starts the execution if it is not already running, then returns a client.
var counter = await factory.GetOrCreateAsync<IPageCounter>("home");
```

### Calling activities from workflow code

All I/O must go through activities. Inside a `[WorkflowUpdate]` or `[WorkflowRun]` method:

```csharp
var nextCount = State.Count + 1;
await ExecuteActivityAsync(
    (PageCounterActivities act) => act.RecordViewAsync(WorkflowId, nextCount),
    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });
State = State with { Count = nextCount };
```

`DurableObjectBase` supplies this helper so workflow code does not need to access the static
`Workflow` class. A bare `await` captures the workflow scheduler; never use
`ConfigureAwait(false)` in workflow code.

### WorkflowRun boilerplate

Every concrete DurableObject must declare `[WorkflowRun]` on the class (not inherited). A typed
state object uses matching snapshot parameters on its initializer and run method:

```csharp
[WorkflowInit]
public PageCounter(DurableObjectSnapshot<PageCounterState>? snapshot = null)
    : base(snapshot, new PageCounterState(0)) { }

[WorkflowRun]
public Task RunAsync(DurableObjectSnapshot<PageCounterState>? snapshot = null) =>
    DurableObjectRunAsync();
```

### Querying asynchronously

```csharp
var count = await client.GetCountAsync();
```

Each run adds three views to `home` and checks the increase. The object remains open after the
host stops; rerunning against the same server demonstrates retained state. The activity only logs
a simulated write. Activities that perform real I/O need their own idempotency policy.
