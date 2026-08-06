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

A multi-project solution template (`temporal-solution`, planned for a later release) will reuse
this exact same precedence via an equivalent shared helper, so Worker and Client projects in that
template never end up pointed at different servers.

## Building from source

The template pack lives in `templates/TemporalCommunity.Templates/`. Packing and verification
follow this repo's usual `just` workflow:

```bash
just pack          # packs TemporalCommunity.Templates.<version>.nupkg into artifacts/packages
just pack-verify    # installs it into an isolated hive and instantiates/builds each template
```
