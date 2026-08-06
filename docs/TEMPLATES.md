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

## Building from source

The template pack lives in `templates/TemporalCommunity.Templates/`. Packing and verification
follow this repo's usual `just` workflow:

```bash
just pack          # packs TemporalCommunity.Templates.<version>.nupkg into artifacts/packages
just pack-verify    # installs it into an isolated hive and instantiates/builds each template
```
