# Observability

A temperature sensor demonstrates how to connect Temporal's client and worker tracing
interceptor to OpenTelemetry. It adds three readings and queries the count and latest value.
`SensorState` is carried through Continue-as-New; rerunning against the same server adds three
more readings to the same sensor.

## Run

Start `temporal server start-dev`, then from the repository root:

```sh
dotnet run --project samples/05-observability
```

The console exporter prints spans as they finish, including client starts/updates/queries,
workflow handling, and `PersistReading` activity execution. Inspect `TraceId` and `ParentSpanId`
to follow a call; exact names and nesting depend on the installed Temporal OpenTelemetry SDK.
The activity logs a simulated persistence operation, not an actual database write.

## Wiring

`Program.cs` registers `AddOpenTelemetry().WithTracing(...)` with the names from
`TracingInterceptor.ClientSource`, `WorkflowsSource`, and `ActivitiesSource`, plus
`AddConsoleExporter()`. It attaches `TracingInterceptor` to `AddTemporalClient`; the SDK also
applies that interceptor to workers using the client. Do not register another instance on the worker, which would duplicate
workflow and activity spans. `AddDurableObjectWorkflows` adds the Durable Objects policy interceptor.

The demo deliberately uses named asynchronous queries. This allows the same pattern with a
compatibility client; sample 01 demonstrates strongly typed generated asynchronous queries.
For a tracing backend, replace the console exporter with an OTLP exporter configured for that
backend's endpoint. Attaching the interceptor does not configure a collector or storage backend.

See the [Temporal tracing interceptor source](https://github.com/temporalio/sdk-dotnet/blob/main/src/Temporalio.Extensions.OpenTelemetry/TracingInterceptor.cs) for the diagnostic sources and span behavior.
