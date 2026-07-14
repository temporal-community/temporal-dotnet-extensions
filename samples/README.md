# Temporal .NET Extensions

## Samples

These samples demonstrate the Durable Objects library in the broader Temporal .NET Extensions
repository.
Each sample is independently runnable. Prerequisites: .NET 10 SDK and a running Temporal server
(`temporal server start-dev`).

## Sample Index

| Sample | Domain Object | Key Concepts | Run Command |
|--------|--------------|--------------|-------------|
| [01-getting-started](01-getting-started/) | `PageCounter` | Generated async client, `IDurableObjectFactory`, lifecycle hooks, activity from update | `just run-sample` |
| [02-input-validation](02-input-validation/) | `BankAccount` | `[WorkflowUpdateValidator]`, `DurableObjectNotFoundException`, `DurableObjectNotActiveException` | `just run-sample 02-input-validation` |
| [03-scheduling](03-scheduling/) | `ReportGenerator` + `SubscriptionTracker` | `CreateDurableObjectScheduleAsync` vs `CreateDurableObjectReminderAsync`, `IReminderReceiver` | `just run-sample 03-scheduling` |
| [04-object-to-object](04-object-to-object/) | `OrderProcessor` → `InventoryTracker` | Activity-mediated DO-to-DO, `IDurableObjectFactory` in activities | `just run-sample 04-object-to-object` |
| [05-observability](05-observability/) | `TemperatureSensor` | OpenTelemetry tracing, `TracingInterceptor`, Temporal Web UI | `just run-sample 05-observability` |
| [06-testing](06-testing/) | `TodoList` | `WorkflowEnvironment.StartLocalAsync()`, xUnit patterns, test isolation | `dotnet test samples/06-testing` |
| [07-analyzer-file-app](07-analyzer-file-app/) | `AnalyzerSampleWorkflow` | File-based app, analyzer diagnostics, deterministic workflow APIs | `dotnet run --file Program.cs` |
| [08-analyzer-project](08-analyzer-project/) | `AnalyzerSampleWorkflow` | IDE diagnostics, code fixes, conventional project restore | `dotnet build` |

## Suggested Reading Order

Start with **01-getting-started** to understand the core programming model (generated client, factory,
lifecycle hooks). Then work through **02-input-validation** to see how errors and validators
compose. After that, pick whichever topic fits your immediate need: scheduling (03), cross-object
communication (04), observability (05), or testing patterns (06).

## Common Prerequisites

- **.NET 10 SDK** — [download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Temporal server** — start with `temporal server start-dev` (requires the [Temporal CLI](https://docs.temporal.io/cli))
- **Temporal Web UI** — available at `http://localhost:8233` once the server is running

> **Note:** If Temporal is not running you will see `Grpc.Core.RpcException` with
> `StatusCode=Unavailable`. Run `temporal server start-dev` and retry.
