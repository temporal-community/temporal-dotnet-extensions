# Temporal .NET Templates

`TemporalCommunity.Templates` provides `dotnet new` templates for plain
[Temporal .NET SDK](https://github.com/temporalio/sdk-dotnet) workflows, activities, and hosts.
Generated code does not depend on the `TemporalCommunity.DurableObjects` runtime.

## Installation

```bash
dotnet new install TemporalCommunity.Templates
```

Use `dotnet new update` to update installed templates, or
`dotnet new uninstall TemporalCommunity.Templates` to remove this package.

## Item templates

Run item templates inside the target project so namespace binding resolves correctly.
They require a `Temporalio` reference and cannot add it to an existing `.csproj`:

```bash
dotnet add package Temporalio
```

### `temporal-workflow`

Creates a plain Temporal workflow class with `[Workflow]` and `[WorkflowRun]`:

```bash
dotnet new temporal-workflow -n OrderWorkflow
```

Without `-n`, the default name is `TemporalWorkflow1`. The namespace binds to the project's
`RootNamespace`.

### `temporal-activity`

Creates a class with one `[Activity]` method:

```bash
dotnet new temporal-activity -n RecordViewActivity
```

Without `-n`, the default name is `TemporalActivity1`. The namespace binds to the project's
`RootNamespace`. The activity method name is derived from the class name, giving each item
a distinct activity type. Task queue routing is configured on the worker, not the item.

### `temporal-converter`

Creates a `DefaultPayloadConverter` subclass and a nested `IEncodingConverter`:

```bash
dotnet new temporal-converter -n MyConverter
```

Implement `TryToPayload` and `ToValue` in `MyConverter.MyConverterEncoding`. The custom encoding
converter sits just before `JsonPlainConverter`; earlier SDK defaults keep their order.
Until implemented, `TryToPayload` returns `false` and values fall through to the defaults.

Configure the client and worker with a matching data converter:

```csharp
var dataConverter = DataConverter.Default with { PayloadConverter = new MyConverter() };
```

The encoding identifier includes the namespace and type name and ends in `/v1`. **It is a
persistent wire contract:** keep it stable for existing payloads and use a new version for
incompatible format changes.

Without `-n`, the default name is `TemporalConverter1`; the namespace binds to `RootNamespace`.
To change only JSON serializer settings, use `new DefaultPayloadConverter(jsonSerializerOptions)`
instead. See [Temporal data conversion best practices](https://docs.temporal.io/develop/dotnet/best-practices/data-handling/data-conversion).

## Project templates

### `temporal-worker`

Creates a console-host Temporal worker using `Temporalio.Extensions.Hosting`:

```bash
dotnet new temporal-worker -n OrderProcessing.Worker
```

| Option | Behavior |
|---|---|
| `--framework <net8.0|net10.0>` | Target framework; defaults to `net10.0`. |
| `--include-otel` | Adds Temporal tracing and an OTLP exporter; defaults to off. |
| `--api` | Uses `WebApplication.CreateBuilder` and the Web SDK, with a root health/status endpoint; defaults to the Generic Host console setup. |

The project includes `Program.cs`, a `TemporalWorkerConnection` helper, and a registered
sample workflow/activity pair. It references `Temporalio`, `Temporalio.Extensions.Hosting`,
and `TemporalCommunity.Extensions.Analyzers`.

Generated projects pin package versions in their `.csproj` files, including Temporalio 1.20.0.
Update those references when upgrading an application; the analyzer version is held in the
`TemporalCommunityAnalyzersVersion` property.

### Configuring the OTLP exporter

`--include-otel` registers the SDK tracing sources using `TracingInterceptor.ClientSource.Name`,
`WorkflowsSource.Name`, `ActivitiesSource.Name`, and `NexusSource.Name`, and adds the client-side
`TracingInterceptor`.
Spans are **exported only when `OTEL_EXPORTER_OTLP_ENDPOINT` is non-blank** at startup:

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
dotnet run
```

Without that setting, the generated host still runs but does not export spans.

## Multi-project templates

### `temporal-solution`

Creates a Worker + Client + Shared solution:

```bash
dotnet new temporal-solution -n Contoso.Fulfillment -o Contoso.Fulfillment
```

When `-n` is omitted, the name is derived from the output/current directory.

| Option | Behavior |
|---|---|
| `--framework <net8.0|net10.0>` | Framework for every generated project; defaults to `net10.0`. |
| `--aspire` | Adds AppHost and ServiceDefaults and provisions a local Temporal dev server; defaults to off. |
| `--otel` | Adds Temporal tracing to Worker and Client. With Aspire, ServiceDefaults supplies the exporter; otherwise a standalone exporter uses the same endpoint gate described above. Defaults to off. |
| `--api` | Generates the Worker with `WebApplication.CreateBuilder`, a root status endpoint, and (with `--aspire`) `/health` and `/alive` endpoints. The Client remains a Generic Host console app. Defaults to off. |

| Project | Purpose |
|---|---|
| `<Name>.Shared` | Workflow/activity types, connection configuration, and the analyzer reference, shared by Worker and Client. |
| `<Name>.Worker` | Console host running the worker. |
| `<Name>.Client` | One-shot console host that starts the sample workflow, prints its result, and exits. |
| `<Name>.AppHost` *(Aspire only)* | Provisions the Temporal dev server and runs Worker and Client as resources. |
| `<Name>.ServiceDefaults` *(Aspire only)* | Service discovery, HTTP resilience, and OpenTelemetry wiring for the console hosts. |

With `--api --aspire`, the Worker uses the ASP.NET Core health endpoint pattern: `/health`
reports readiness and `/alive` reports liveness during development. The AppHost polls the Worker's
`/health` endpoint as its resource health check. The generated Worker does not add OpenAPI; add it
to the Web project if your application needs an API description.

### Run a generated solution

For a standalone solution, start a Temporal server separately (or configure a remote one), then
run Worker and Client in separate terminals:

```bash
dotnet new temporal-solution -n Contoso.Fulfillment -o Contoso.Fulfillment
cd Contoso.Fulfillment
temporal server start-dev
# In another terminal, from the generated solution directory:
dotnet run --project Contoso.Fulfillment.Worker
# In a third terminal, from the same directory:
dotnet run --project Contoso.Fulfillment.Client
```

The local-server path requires the [Temporal CLI](https://docs.temporal.io/cli).
For an Aspire-enabled solution:

```bash
dotnet new temporal-solution -n Contoso.Fulfillment -o Contoso.Fulfillment --aspire
cd Contoso.Fulfillment
dotnet run --project Contoso.Fulfillment.AppHost
```

Aspire provisions the Temporal dev server without a separately installed Temporal CLI. The
AppHost enables the Aspire CLI bundle; if launch cannot find a compatible CLI, use the .NET 10
SDK or install the [Aspire CLI](https://aspire.dev/get-started/install-cli/).
See [Aspire launch documentation](https://aspire.dev/get-started/aspire-sdk/) for other launch modes.
Build the solution before using `aspire start`, which launches resource projects with `--no-build`.

## Connecting to Temporal

Both project templates resolve their connection with this precedence:

1. **Environment variables or a Temporal CLI profile.** `ClientEnvConfig.LoadClientConnectOptions`
   loads `TEMPORAL_ADDRESS`, `TEMPORAL_NAMESPACE`, and other settings, or a profile from
   `temporal.toml`. If it supplies a non-blank target host, that host wins.
2. **`Temporal:Address` configuration.** Used only if step 1 supplied no target host.
   Missing or blank values fall through to step 3.
3. **`localhost:7233`.**

**Temporal Cloud credentials, TLS, namespace, and RPC metadata come from environment/profile
configuration**, not `Temporal:Address`, which controls only the host. Configure a profile or
the relevant `TEMPORAL_*` variables for Cloud connections.

The solution's Worker and Client share `SharedTemporalConnection`; configure both hosts
consistently to connect to the same server. Aspire's resource reference supplies
`TEMPORAL_ADDRESS` and `TEMPORAL_NAMESPACE` to both hosts for local development.
The generated registration preserves resolved connection settings and host logging. With
tracing enabled, it registers a single tracing interceptor.
The client connects on first use.

### Task queues and application routing

The default task queue is `<Name>-tq`: `temporal-worker -n OrderProcessing.Worker` uses
`OrderProcessing.Worker-tq`, and `temporal-solution -n Alpha` uses `Alpha-tq` for both Worker and
Client. The standalone `temporal-worker` uses a hardcoded `taskQueue` constant in `Program.cs`;
edit that string to choose a different queue.

The solution Worker and Client both use hardcoded `taskQueue` constants. Keep the values matched
when changing the generated application.

Changing an existing application's queue is a routing migration: deploy compatible consumers,
drain or complete work on the old queue, and only then remove the old routing.

### Client lifecycle and restore behavior

The solution Client starts the sample workflow, waits for its result, logs it, and requests host
shutdown. Both RPCs receive the host stopping token. Local shutdown cancellation is a normal
stop; workflow or RPC failures are logged and produce a nonzero exit status.
Local RPC cancellation does not cancel the server-side workflow.

Both project templates restore generated projects after creation.
