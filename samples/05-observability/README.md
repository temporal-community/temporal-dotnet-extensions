# Sample 05 — Observability

This sample demonstrates OpenTelemetry tracing integration with DurableObjects, using a
`TemperatureSensor` object that records IoT readings and produces distributed traces.

The sample intentionally calls the named async query API so its tracing output shows that raw
client path. Sample 01 demonstrates the generated strongly typed async-query client.

## What you'll learn

- How to attach `TracingInterceptor` to both the Temporal client and the worker
- How workflow tasks, update executions, and activity calls appear as nested OTel spans
- How to wire `OpenTelemetry.Exporter.Console` for local development
- How to connect Jaeger for a full production-grade trace view

## Prerequisites

- [Temporal server running locally](https://docs.temporal.io/cli#start-dev-server): `temporal server start-dev`
- .NET 10 SDK
- (Optional) Jaeger for a visual trace UI — see below

## Running

```bash
cd samples/05-observability
dotnet run
```

## Reading the console OTel output

The `ConsoleExporter` prints each span as it closes. When you record a reading you will see
three nested spans:

```
Activity.DisplayName:   temporal:StartWorkflow
Activity.DisplayName:   temporal:ExecuteUpdate       (update:RecordReadingAsync)
Activity.DisplayName:   temporal:RunActivity         (activity:PersistReadingAsync)
```

The parent-child relationship (via `ParentId`) shows:
- The client call starts a workflow span.
- The worker's update handler is a child of the workflow task span.
- Each activity invocation is a child of the update span.

## Connecting Jaeger

Run Jaeger all-in-one (Docker):

```bash
docker run -d --name jaeger \
  -p 16686:16686 \
  -p 4317:4317 \
  jaegertracing/all-in-one:latest
```

Replace `AddConsoleExporter()` in `Program.cs` with an OTLP exporter:

```xml
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.*" />
```

```csharp
.WithTracing(tracing => tracing
    .AddSource("Temporalio")
    .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317")));
```

Open http://localhost:16686 and search for service `temporal`.

## Key wiring — TracingInterceptor on both client and worker

The interceptor must be attached in two places:

```csharp
// Client side — wraps outbound RPCs (StartWorkflow, ExecuteUpdate, Query).
builder.Services.AddTemporalClient(opts =>
{
    opts.TargetHost = "localhost:7233";
    opts.Interceptors = [new TracingInterceptor()];
});

// Worker side — wraps inbound workflow tasks and activity invocations.
builder.Services.AddHostedTemporalWorker(taskQueue)
    .ConfigureOptions(opts => opts.Interceptors = [new TracingInterceptor()])
    .AddDurableObjectWorkflows(Assembly.GetExecutingAssembly())
    .AddAllActivities<SensorActivities>();
```

`AddDurableObjectWorkflows` appends `DurableObjectWorkerInterceptor` after any interceptors
already in `opts.Interceptors`. This means `TracingInterceptor` is registered first in the
interceptors array, making it the **outermost span** — its span starts before any other
interceptor runs and ends after all others complete. The `DurableObjectWorkerInterceptor` runs
inside it, so update executions and activity calls appear as child spans under the tracing span.
This is the correct nesting order for observability.
