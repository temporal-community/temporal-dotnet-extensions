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

### `temporal-payload-converter`

Creates a new `IEncodingConverter` implementation for a custom payload encoding.

```bash
dotnet new temporal-payload-converter -n MyEncodingConverter
```

Omit `-n` to get the default placeholder name `TemporalPayloadConverter1`. The generated class's
namespace is bound to the target project's `RootNamespace`.

The generated `IEncodingConverter` implementation is meant to be composed into a
`DefaultPayloadConverter` alongside the built-in converters — order matters, since converters are
tried in order when converting to a payload, so put custom ones first:

```csharp
var payloadConverter = new DefaultPayloadConverter(
    new MyEncodingConverter(),
    new BinaryNullConverter(),
    new BinaryPlainConverter(),
    new JsonProtoConverter(),
    new BinaryProtoConverter(),
    new JsonPlainConverter(new System.Text.Json.JsonSerializerOptions()));
var dataConverter = DataConverter.Default with { PayloadConverter = payloadConverter };
// Set dataConverter on TemporalClientConnectOptions.DataConverter (client) and/or
// TemporalWorkerOptions.DataConverter (worker) before connecting/running.
```

Composing into `DefaultPayloadConverter` is not the only supported customization path — the SDK's
own `DefaultPayloadConverter` doc comment notes that subclassing `DefaultPayloadConverter` directly
is also valid, and is a better fit if you need to change more than a single encoding (e.g.
reordering or replacing several of the built-in converters at once). The item template scaffolds
an `IEncodingConverter` because composition is the more surgical fit for adding one custom
encoding to an existing project.

The target project must already reference the `Temporalio` package — item templates cannot add a
`PackageReference` to an existing `.csproj`. Add it first if needed:

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
| `TemporalConnection.cs` | `public static class TemporalConnection` with the `Resolve(...)` method described below. |
| `Workflows/SampleWorkflow.cs`, `Activities/SampleActivities.cs` | A starter `[Workflow]`/`[Activity]` pair, wired up in `Program.cs` via `AddWorkflow<T>()`/`AddScopedActivities<T>()`. |

**Package version pins.** `Temporalio`, `Temporalio.Extensions.Hosting`,
`Temporalio.Extensions.OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`, and
`OpenTelemetry.Exporter.OpenTelemetryProtocol` are pinned to exact versions directly in the
generated `.csproj`'s `PackageReference` items — bump them there when newer stable releases ship.
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
dotnet new temporal-solution -n Contoso.Fulfillment
```

Options:

- `--framework <net8.0|net10.0>` (default `net10.0`) — applied to Worker, Client, and Shared.
- `--include-aspire` (default off) — adds an `AppHost` and `ServiceDefaults` project. The AppHost
  references `TemporalCommunity.Aspire.Hosting` and calls `AddTemporalLocalDevServer`, which
  auto-provisions a local Temporal dev server as part of `aspire run`/`dotnet run` — no separately
  installed Temporal CLI required for this path (it uses
  `Temporalio.Testing.WorkflowEnvironment`'s self-managed ephemeral server under the hood). With
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

**Non-trivial project names.** A single `sourceName` token replace isn't enough for a
multi-project solution with strongly-typed Aspire project references and XML project files, so two
additional symbols handle the edge cases: `GeneratedClassNamePrefix` sanitizes the name into a
valid C# identifier (used for `Projects.<Name>_Worker`-style references in `AppHost.cs`), and
`XmlEncodedProjectName` XML-escapes the name for use inside `.csproj` `ProjectReference` paths.
Both track the folder names on disk (which use the raw, unescaped name), so a name like
`Contoso-Fulfillment&Orders` produces valid C# (`Contoso_Fulfillment_Orders`) and valid XML
(`Contoso-Fulfillment&amp;Orders`) even though the actual directory on disk keeps the literal `&`.

**Package version pins** follow the same one-place-per-package convention as `temporal-worker`.

## Connecting to Temporal

`temporal-worker`'s generated `TemporalConnection.cs` resolves a `TemporalClientConnectOptions`
using a fixed three-step precedence, implemented in `TemporalConnection.Resolve(IConfiguration,
ClientEnvConfig.ProfileLoadOptions?)`:

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

## Building from source

The template pack lives in `templates/TemporalCommunity.Templates/`. Packing and verification
follow this repo's usual `just` workflow:

```bash
just pack                          # packs TemporalCommunity.Templates.<version>.nupkg into artifacts/packages
just pack-verify                   # installs it into an isolated hive and instantiates/builds each template
just template-smoke-test-standalone  # real Temporal dev server + Worker + Client runtime check
just template-smoke-test-aspire      # real Aspire AppHost + auto-provisioned dev server runtime check
just template-smoke-test             # runs both of the above
```
