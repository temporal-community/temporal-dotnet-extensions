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
