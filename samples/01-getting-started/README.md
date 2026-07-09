# Sample 01 — Getting Started

This sample introduces the core DurableObjects programming model through a `PageCounter` object
that tracks page views per URL slug.

## Why DurableObjects?

Persistent state in distributed systems usually means a database row + a background worker +
a polling loop. DurableObjects replace that pattern with a single addressable object that retains
its state as long as it's needed, survives worker crashes automatically, and executes operations
exactly once — with a full audit trail in its workflow history. If you've used Orleans grains or
Akka.NET actors, DurableObjects are conceptually similar but backed by Temporal's durable
execution engine instead of an in-process scheduler.

## What you'll learn

- How to define a DurableObject contract (`IPageCounter : IDurableObject`)
- How to implement a DurableObject (`PageCounter : DurableObjectBase`)
- The required `[WorkflowRun]` boilerplate on the concrete class
- How to call activities from workflow code (`Workflow.ExecuteActivityAsync`)
- How to wire everything up with `Microsoft.Extensions.Hosting`
- How to obtain a proxy via `IDurableObjectFactory`

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

### Proxy vs. GetOrCreateAsync

```csharp
// No RPC — just a local dispatch facade.
var proxy = factory.Get<IPageCounter>("home");

// Issues an update-with-start RPC: atomically creates the execution if it doesn't exist.
var counter = await factory.GetOrCreateAsync<IPageCounter>("home");
```

### Calling activities from workflow code

All I/O must go through activities. Inside a `[WorkflowUpdate]` or `[WorkflowRun]` method:

```csharp
await Workflow.ExecuteActivityAsync(
    (PageCounterActivities act) => act.RecordViewAsync(slug, count),
    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) })
    .ConfigureAwait(true); // ConfigureAwait(true) is required in workflow code
```

### WorkflowRun boilerplate

Every concrete DurableObject must declare `[WorkflowRun]` on the class (not inherited):

```csharp
[WorkflowRun]
public Task RunAsync() => DurableObjectRunAsync();
```

### Querying asynchronously

```csharp
var count = await factory.QueryDurableObjectAsync<int>("home", "GetCount");
```
