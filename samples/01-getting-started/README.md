# Sample 01 — Getting Started

This sample introduces the core DurableObjects programming model through a `PageCounter` object
that tracks page views per URL slug.

## Why DurableObjects?

Per-entity state in distributed systems is often kept in a database row and coordinated by a
background worker and a polling loop. A DurableObject puts that state and its update logic in a
single addressable object that retains its state as long as it's needed, recovers after worker
crashes, and records operations in Temporal workflow history. It does not replace your database:
in this sample, the count lives in the object, and an activity stands in for the write to a
database or analytics service. If you've used Orleans grains or
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

### Use one generated client

```csharp
// No RPC — creates a concrete generated client with async queries.
var client = factory.GetPageCounterClient("home");

// Updates use Update-with-Start, so update directly when the operation should
// materialize a missing object.
await client.IncrementAsync();

// The update guarantees the object now exists. Use the generated async query
// method in service code instead of the synchronous contract query.
var count = await client.GetCountAsync();
```

Only issue an explicit start when the first operation must be a query against an object that might
not exist. Keep using the same generated client:

```csharp
var client = factory.GetPageCounterClient("home");
await factory.GetOrCreateAsync<IPageCounter>("home");
var count = await client.GetCountAsync();
```

`GetOrCreateAsync` confirms that an execution exists, but a direct generated update avoids that
extra start RPC when the update itself is intended to materialize the object.

### Worker and task queue

Use a dedicated Temporal worker and task queue for Durable Objects. Register the factory and worker
with the same queue name; `AddDurableObjectWorkflows` discovers object workflows and installs the
Durable Objects worker policies:

```csharp
const string taskQueue = "getting-started-tq";
builder.Services.AddDurableObjects(taskQueue);
builder.Services.AddHostedTemporalWorker(taskQueue)
    .AddDurableObjectWorkflows(typeof(PageCounter).Assembly);
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
