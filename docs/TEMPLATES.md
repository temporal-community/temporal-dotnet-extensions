# Temporal .NET Templates

`TemporalCommunity.Templates` provides `dotnet new` templates for scaffolding Temporal .NET code.
Generated code is plain, idiomatic [Temporal .NET SDK](https://github.com/temporalio/sdk-dotnet)
(`[Workflow]`/`[WorkflowRun]`, `[Activity]`) — it does not depend on the
`TemporalCommunity.DurableObjects` runtime. Use these templates as a starting point for any
Temporal .NET project, whether or not you adopt Durable Objects elsewhere.

## Installation

```bash
dotnet new install TemporalCommunity.Templates
```

Update to the latest version:

```bash
dotnet new update
```

Uninstall:

```bash
dotnet new uninstall TemporalCommunity.Templates
```

## Item templates

Item templates add a single file to an existing project. Run them from inside the target project's
directory (or a subdirectory of it) so namespace binding resolves correctly.

### `temporal-workflow`

Creates a new plain Temporal workflow class (`[Workflow]`/`[WorkflowRun]`).

```bash
dotnet new temporal-workflow -n OrderWorkflow
```

Omit `-n` to get the default placeholder name `TemporalWorkflow1`. The generated class's namespace
is bound to the target project's `RootNamespace`.

### `temporal-activity`

Creates a new plain Temporal activity class (`[Activity]`).

```bash
dotnet new temporal-activity -n RecordViewActivity
```

Omit `-n` to get the default placeholder name `TemporalActivity1`. The generated class's namespace
is bound to the target project's `RootNamespace`. Activities (unlike workflow code) can freely do
I/O, use `ILogger`, and access DI — they run outside the workflow scheduler.

The generated method name is derived from the requested class name, so each activity item has a
distinct Temporal activity type. The item template deliberately contains one activity method and
does not configure a task queue; queue routing belongs to a Worker.

### `temporal-converter`

Creates a custom payload converter with a nested encoding class in one file:

- `MyConverter.MyConverterEncoding` — a nested `IEncodingConverter` for one custom wire format. Implement
  `TryToPayload` and `ToValue` here.
- `MyConverter` — a `DefaultPayloadConverter` subclass that keeps the SDK's default encoding
  converters in their default order and puts `MyConverter.MyConverterEncoding` just before
  `JsonPlainConverter`. Converters are tried in order, and `JsonPlainConverter` accepts any value.

```bash
dotnet new temporal-converter -n MyConverter
```

Omit `-n` to get the default name `TemporalConverter1`. The generated classes' namespace is bound
to the target project's `RootNamespace`. Pass the converter through a data converter in the
Temporal client or worker options:

```csharp
var dataConverter = DataConverter.Default with { PayloadConverter = new MyConverter() };
```

Until `TryToPayload` is implemented it returns `false`, so values fall through to the default
converters.

The generated encoding identifier includes the namespace and type name and ends in `/v1`. It is a
persistent wire contract; keep it stable for existing payloads and add a new version if the format
changes incompatibly.

To change only JSON serializer settings, you don't need this template; use
`new DefaultPayloadConverter(jsonSerializerOptions)` instead.

See [Temporal data conversion best practices](https://docs.temporal.io/develop/dotnet/best-practices/data-handling/data-conversion).

All item templates require the target project to reference the `Temporalio` package; they cannot
add a `PackageReference` to an existing `.csproj`. Add it first if needed:

```bash
dotnet add package Temporalio
```

## Project templates

Project templates generate a complete, standalone project rather than a single file.

### `temporal-worker`

Creates a single console-host project running a Temporal worker
(`Temporalio.Extensions.Hosting`), with a resolved connection and an optional OpenTelemetry setup.

```bash
dotnet new temporal-worker -n OrderProcessing.Worker
```

Options:

- `--framework <net8.0|net10.0>` (default `net10.0`) — the target framework.
- `--include-otel` (default off) — adds a `"Temporalio"` `ActivitySource`, the client-side
  `TracingInterceptor`, and an OTLP exporter to the generated project.

Generated files:

| File | Purpose |
|---|---|
| `<Name>.csproj` | Console project referencing `Temporalio`, `Temporalio.Extensions.Hosting`, and `TemporalCommunity.Extensions.Analyzers`. |
| `Program.cs` | Host setup: resolves connection options, registers `ITemporalClient`, and registers the worker. |
| `TemporalWorkerConnection.cs` | `public static class TemporalWorkerConnection` with the `Resolve(...)` method described below. |
| `Workflows/SampleWorkflow.cs`, `Activities/SampleActivities.cs` | A starter `[Workflow]`/`[Activity]` pair, wired up in `Program.cs` via `AddWorkflow<T>()`/`AddScopedActivities<T>()`. |

**Package version pins.** `Temporalio`, `Temporalio.Extensions.Hosting`,
`Temporalio.Extensions.OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`, and
`OpenTelemetry.Exporter.OpenTelemetryProtocol` are pinned to exact versions directly in the
generated `.csproj`'s `PackageReference` items — bump them there when newer stable releases ship.
The current generated project templates use Temporalio 1.20.0.
The `TemporalCommunity.Extensions.Analyzers` version is pinned once, in a
`TemporalCommunityAnalyzersVersion` MSBuild property near the top of the same `.csproj`, so bumping
it is a one-line change independent of this template package's own version.

**Configuring the OTLP exporter.** `--include-otel` wires up span creation, but spans are only
*exported* when the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable is set — the generated
`Program.cs` checks it at startup and only calls `UseOtlpExporter()` when it's present (the same
gating convention Aspire's own `ServiceDefaults` project template uses, so the same environment
variable works whether or not this project later adopts Aspire). Point it at a local collector,
Jaeger, or Grafana Tempo endpoint before running the generated worker, e.g.:

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
dotnet run
```

Without that variable set, `--include-otel` still compiles and runs — it just doesn't send spans
anywhere.

## Multi-project templates

### `temporal-solution`

Creates a Worker + Client + Shared multi-project solution running plain Temporalio
workflows/activities, with optional .NET Aspire orchestration.

```bash
dotnet new temporal-solution -n Contoso.Fulfillment -o Contoso.Fulfillment
```

When `-n` is omitted, the generated name is derived from the output/current directory; it is not
automatically `TemporalSolution.1` because this template does not set `preferDefaultName: true`.
Use `-n TemporalSolution.1` explicitly when testing the digit-after-dot naming case.

Options:

- `--framework <net8.0|net10.0>` (default `net10.0`) — applied to Worker, Client, and Shared,
  plus AppHost and ServiceDefaults when `--include-aspire` is enabled.
- `--include-aspire` (default off) — adds an `AppHost` and `ServiceDefaults` project. The AppHost
  references `TemporalCommunity.Aspire.Hosting` and calls `AddTemporalLocalDevServer`, which
  auto-provisions a local Temporal dev server when the AppHost starts — no separately
  installed Temporal CLI required for this path. With
  `--include-aspire` off (the default), you run your own `temporal server start-dev` (or point at
  Temporal Cloud), exactly like `temporal-worker` and every sample in `samples/`.
- `--include-otel` (default off) — adds a `"Temporalio"` `ActivitySource` and the client-side
  `TracingInterceptor` to Worker and Client. When combined with `--include-aspire`, the exporter
  comes from `ServiceDefaults`; without it, a standalone OTLP exporter is added instead (see
  "Configuring the OTLP exporter" above — the same `OTEL_EXPORTER_OTLP_ENDPOINT` convention applies).

Generated projects:

| Project | Purpose |
|---|---|
| `<Name>.Shared` | Class library holding `SharedTemporalConnection` and the starter `[Workflow]`/`[Activity]` pair. Referenced by both Worker and Client — Client needs it for type-safe `StartWorkflowAsync<SampleWorkflow>(...)` calls. Carries the `TemporalCommunity.Extensions.Analyzers` reference, since that's where the workflow/activity code lives. |
| `<Name>.Worker` | Console host running the Temporal worker, referencing Shared. |
| `<Name>.Client` | Console host that starts the sample workflow, waits for its result, prints it, and exits — a one-shot demo, not a long-running service. |
| `<Name>.AppHost` *(--include-aspire only)* | Aspire orchestrator: provisions the local Temporal dev server and runs Worker + Client as Aspire resources. |
| `<Name>.ServiceDefaults` *(--include-aspire only)* | Service discovery, HTTP resilience, and OpenTelemetry wiring shared by Worker and Client. Trimmed from Aspire's own ServiceDefaults template to what applies to plain Generic Host console apps — no ASP.NET Core instrumentation or health-check endpoints, since neither Worker nor Client is a web application. |

### Run a generated solution

For a standalone solution, start a Temporal server separately (or configure a remote one), then
run the Worker and Client in separate terminals:

```bash
dotnet new temporal-solution -n Contoso.Fulfillment -o Contoso.Fulfillment
cd Contoso.Fulfillment
temporal server start-dev
# In another terminal, from the generated solution directory:
dotnet run --project Contoso.Fulfillment.Worker
# In a third terminal, from the same directory:
dotnet run --project Contoso.Fulfillment.Client
```

The standalone example requires the [Temporal CLI](https://docs.temporal.io/cli) for
`temporal server start-dev`; if using a remote server instead, configure the connection as
described below. The generated Client is a one-shot demo; the Worker keeps running.

For an Aspire-enabled solution, use **one** of these launch methods from the generated solution
directory:

```bash
dotnet new temporal-solution -n Contoso.Fulfillment -o Contoso.Fulfillment --include-aspire
cd Contoso.Fulfillment
dotnet run --project Contoso.Fulfillment.AppHost
# Or, with the Aspire CLI installed:
aspire run --apphost Contoso.Fulfillment.AppHost/Contoso.Fulfillment.AppHost.csproj
# Or, with the Aspire CLI installed, for a background session:
dotnet build
aspire start --apphost Contoso.Fulfillment.AppHost/Contoso.Fulfillment.AppHost.csproj
```

The AppHost sets `AspireUseCliBundle=true`. For `dotnet run`, the Aspire SDK selects an installed
compatible `aspire` on `PATH` when available, or invokes the Aspire CLI version paired with the
AppHost SDK through DNX. The DNX fallback requires the .NET 10 SDK and access to the configured
NuGet sources. `aspire run` and `aspire start` instead require the
[Aspire CLI](https://aspire.dev/get-started/install-cli/) installed and on `PATH`; use
`aspire stop --apphost Contoso.Fulfillment.AppHost/Contoso.Fulfillment.AppHost.csproj` to stop a
background session. Aspire mode provisions the Temporal dev server through
`TemporalCommunity.Aspire.Hosting`; it does **not** require a separately installed Temporal CLI.
Build the generated solution before `aspire start` so its resource projects have all referenced
assemblies in their output; `aspire start` launches those projects with `--no-build`.
See the official [Aspire SDK CLI-bundle and launch documentation](https://aspire.dev/get-started/aspire-sdk/)
and [DNX documentation](https://learn.microsoft.com/dotnet/core/tools/dotnet-tool-exec).

If `dotnet run` cannot find a compatible installed Aspire CLI, check that .NET 10's `dnx` is
available and that NuGet restore can reach its sources; alternatively install the Aspire CLI.
If `aspire run` or `aspire start` is not recognized, install the Aspire CLI first. A successful
restore or build does not, by itself, confirm the AppHost or the Temporal dev server started.

**Non-trivial project names.** A single `sourceName` token replace isn't enough for a
multi-project solution with strongly-typed Aspire project references and XML project files, so
separate symbols handle the edge cases: `GeneratedNamespacePrefix` preserves a valid C# namespace
prefix, `GeneratedAspirePrefix` sanitizes the name into a valid identifier (used for
`Projects.<Name>_Worker`-style references in `AppHost.cs`), and `XmlEncodedProjectName` XML-escapes
the name for use inside `.csproj` `ProjectReference` paths.
Both track the folder names on disk (which use the raw, unescaped name), so a name like
`Contoso-Fulfillment&Orders` produces valid C# (`Contoso_Fulfillment_Orders`) and valid XML
(`Contoso-Fulfillment&amp;Orders`) even though the actual directory on disk keeps the literal `&`.

The namespace and Aspire identifiers are intentionally separate. Dots in a C# namespace are
preserved, C# keyword segments are escaped with `@`, and invalid or digit-leading segments are
sanitized for C#. Aspire's `Projects.*` identifiers use a separate flattened valid identifier:

| `-n` | C# namespace prefix | Aspire project prefix |
|---|---|---|
| `class` | `@class` | `class` |
| `Acme.class` | `Acme.@class` | `Acme_class` |
| `Contoso.Fulfillment` | `Contoso.Fulfillment` | `Contoso_Fulfillment` |
| `TemporalSolution.1` | `TemporalSolution._1` | `TemporalSolution__1` |

Omitting `-n` is a separate case: the CLI derives the name from the output/current directory
instead of using the template's `defaultName` (`TemporalSolution.1`), because
`preferDefaultName` is not enabled.

**Package version pins** follow the same one-place-per-package convention as `temporal-worker`.
Generated project templates pin Temporalio 1.20.0. When upgrading from a version before 1.18.0,
note that workers now enforce outbound payload/memo size limits before sending: over-limit task
completions fail retryably instead of reaching the server and failing non-retryably. Review payload
size warnings (`TMPRL1103`) and [Temporal .NET SDK 1.18.0 release notes](https://github.com/temporalio/sdk-dotnet/blob/main/CHANGELOG.md#1180---2026-08-13)
before upgrading workloads that use large payloads or a size-changing payload proxy.

## Connecting to Temporal

`temporal-worker`'s generated `TemporalWorkerConnection.cs` resolves a
`TemporalClientConnectOptions` using a fixed three-step precedence, implemented in
`TemporalWorkerConnection.Resolve(IConfiguration, ClientEnvConfig.ProfileLoadOptions?)`:

1. **Environment variables or a Temporal CLI profile** — `ClientEnvConfig.LoadClientConnectOptions(...)`
   picks up `TEMPORAL_ADDRESS`/`TEMPORAL_NAMESPACE`/etc., or a named profile from a
   `temporal.toml` file. If this already supplies a target host, it wins outright — step 2 below is
   never consulted.
2. **`Temporal:Address` configuration** — only when step 1 found no target host. A present-but-blank
   value (e.g. an empty string from a config source) is treated the same as a missing one and falls
   through to step 3, rather than being used as-is.
3. **`localhost:7233`** — the final fallback, matching every sample in `samples/` that assumes a
   locally running `temporal server start-dev`.

**Temporal Cloud credentials, TLS settings, namespace, and RPC metadata always come from step 1**
(environment variables or a profile) — never from `Temporal:Address`, which controls only the
target host. To connect to Temporal Cloud, configure a Temporal CLI profile or the relevant
`TEMPORAL_*` environment variables; do not try to express an API key or TLS setting through
`Temporal:Address`, since `Resolve` never reads anything else from it.

The `temporal-solution` multi-project template reuses this exact same precedence via an equivalent
`SharedTemporalConnection` helper in its `Shared` project, so Worker and Client never end up
pointed at different servers. When `--include-aspire` is enabled, `AddTemporalLocalDevServer`'s
`WithReference` injects `TEMPORAL_ADDRESS`/`TEMPORAL_NAMESPACE` env vars into both projects, which
is what typically satisfies step 1 above — Temporal Cloud credentials, TLS, namespace, and RPC
metadata still come from environment/profile configuration, not from anything Aspire-specific.

Every generated host (`temporal-worker`, and `temporal-solution`'s Worker and Client) registers
`ITemporalClient` through the SDK's `AddTemporalClient(Action<TemporalClientConnectOptions>)` from
`Temporalio.Extensions.Hosting`. Because that callback mutates the SDK's own options instance
rather than replacing it, the generated `ApplyTo(resolved, options)` helper next to `Resolve`
explicitly copies every resolved connection and client setting — target host, namespace, TLS,
API key, RPC metadata and binary metadata, RPC retry, keepalive, HTTP CONNECT proxy, DNS load
balancing, gRPC compression, payload limits, identity, runtime, data converter, interceptors, query reject
condition, and plugins. It deliberately leaves `LoggerFactory` alone so the host's
`ILoggerFactory`, which the SDK assigns first, is kept. With `--include-otel`, the
`TracingInterceptor` is appended after any interceptors already present, exactly once. The SDK
creates the client lazily, so it connects on first use.

### Task queues and application routing

Each generated project application receives a stable, name-derived default task queue rather than
a fixed literal shared by every generated project, so independent generated applications in the
same Temporal namespace do not collide by default. The default is the template's substituted name
followed by `-tq`, with no additional prefix — for example, `temporal-worker -n
OrderProcessing.Worker` defaults to `OrderProcessing.Worker-tq`, and `temporal-solution -n Alpha`
defaults to `Alpha-tq` for both the generated Worker and Client, since both derive the default from
the same generated name and always agree when no override is configured. All generated project
applications read the same configuration key:

```text
Temporal:TaskQueue
```

A non-empty `Temporal:TaskQueue` value overrides the applicable default. A missing or `null` value
uses the default. A blank or whitespace-only value is rejected explicitly with
`InvalidOperationException`:

```text
Configuration value 'Temporal:TaskQueue' must not be blank. Set it to a valid task queue name or remove it.
```

Configure the value through the normal .NET configuration providers when deploying. Changing a
queue for an already deployed application is a routing migration: deploy compatible consumers,
drain or complete work on the old queue, and only then remove the old routing.

The standalone Worker and solution Worker use separate generated connection helpers. The standalone
helper is `TemporalWorkerConnection`, avoiding ambiguity with the Temporal SDK's own
`TemporalConnection` type. The solution helper remains `SharedTemporalConnection` because it is
intentionally shared by the Worker and Client.

### Client lifecycle and restore behavior

The generated solution Client is a one-shot `BackgroundService`: it starts the sample workflow,
waits for the result, logs it, and requests host shutdown. It passes the host stopping token to
both the start RPC and the separate result-wait RPC. Cancellation caused by ordinary local
shutdown is treated as a normal stop; genuine workflow or RPC failures are logged and produce a
nonzero process exit status. Local RPC cancellation does not cancel the server-side workflow.

Both `temporal-worker` and `temporal-solution` restore their generated projects after creation.
