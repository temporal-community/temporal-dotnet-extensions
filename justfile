# ──────────────────────────────────────────────────────────
# Temporal DurableObjects — justfile
# https://github.com/casey/just
# ──────────────────────────────────────────────────────────

set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]

solution              := "TemporalDurableObjects.slnx"
configuration         := "Release"
artifacts_dir         := "artifacts/packages"
coverage_dir          := "artifacts/coverage"
unit_tests_dir        := "tests/TemporalCommunity.DurableObjects.Tests"
integration_tests_dir := "tests/TemporalCommunity.DurableObjects.IntegrationTests"
general_analyzer_tests_dir := "tests/TemporalCommunity.Extensions.Analyzers.Tests"
durable_analyzer_tests_dir := "tests/TemporalCommunity.DurableObjects.Analyzers.Tests"
generated_client_tests_dir := "tests/TemporalCommunity.DurableObjects.GeneratedClients.Tests"
general_codefix_tests_dir := "tests/TemporalCommunity.Extensions.Analyzers.CodeFixes.Tests"
durable_codefix_tests_dir := "tests/TemporalCommunity.DurableObjects.Analyzers.CodeFixes.Tests"
template_tests_dir    := "tests/TemporalCommunity.Templates.Tests"
benchmarks_dir        := "benchmarks/TemporalCommunity.DurableObjects.Benchmarks"
aot_smoke_dir         := "tests/smoke/GeneratedClientAot"
# Runs minver (local tool — .config/dotnet-tools.json) to compute the current version from git tags.
# The sed/tr reads MinVerDefaultPreReleaseIdentifiers from Directory.Build.props so the pre-release
# label has a single source of truth; minver-cli must be installed via `dotnet tool restore`.
version               := `dotnet tool run minver --tag-prefix "" --default-pre-release-identifiers $(sed -n 's/.*<MinVerDefaultPreReleaseIdentifiers>\(.*\)<\/MinVerDefaultPreReleaseIdentifiers>.*/\1/p' Directory.Build.props | tr -d ' ')`

# ── Meta ──────────────────────────────────────────────────

# List all recipes
default:
    @just --list

# Show project info (version, solution, configuration, artifacts path)
info:
    @echo "Solution:      {{solution}}"
    @echo "Version:       {{version}}"
    @echo "Configuration: {{configuration}}"
    @echo "Artifacts:     {{artifacts_dir}}"

# Check required tools and services are available
doctor:
    @echo "==> dotnet"
    dotnet --version
    @echo "==> just"
    just --version
    @echo "==> temporal CLI"
    temporal --version
    @echo "==> Temporal server reachability"
    -temporal workflow list --namespace default --limit 1 2>&1 || echo "⚠  Temporal server not reachable (required to run SAMPLES; integration tests use an embedded server via WorkflowEnvironment.StartLocalAsync)"
    @echo "==> minver-cli (local tool)"
    dotnet tool run minver --version

# Publish and execute the generated-client smoke application under NativeAOT.
[unix]
aot-verify:
    dotnet publish "{{aot_smoke_dir}}/GeneratedClientAot.csproj" --configuration Release --nologo --output "{{aot_smoke_dir}}/bin/aot-publish"
    "{{aot_smoke_dir}}/bin/aot-publish/GeneratedClientAot"

# ── Clean ─────────────────────────────────────────────────

# Remove all build outputs and artifacts
clean: clean-source clean-tests
    rm -rf "{{artifacts_dir}}"
    rm -rf "{{coverage_dir}}"

# Remove bin/obj from all projects
clean-source:
    dotnet clean "{{solution}}" --configuration "{{configuration}}" --nologo -v minimal

# [unix] Remove stale .trx and .coverage files from test output dirs
[unix]
clean-tests:
    find "{{unit_tests_dir}}" -name "*.trx" -delete 2>/dev/null || true
    find "{{integration_tests_dir}}" -name "*.trx" -delete 2>/dev/null || true
    find "{{unit_tests_dir}}" -name "*.coverage" -delete 2>/dev/null || true
    find "{{integration_tests_dir}}" -name "*.coverage" -delete 2>/dev/null || true

# [windows] Remove stale test artifacts (PowerShell)
[windows]
clean-tests:
    Get-ChildItem -Path "{{unit_tests_dir}}", "{{integration_tests_dir}}" -Include "*.trx", "*.coverage" -Recurse | Remove-Item -Force

# ── Build ─────────────────────────────────────────────────

# Restore NuGet packages
restore:
    dotnet restore "{{solution}}"

# Build in Release mode
build: restore
    dotnet build "{{solution}}" --configuration "{{configuration}}" --no-restore --nologo

# Build in Debug mode
build-debug: restore
    dotnet build "{{solution}}" --configuration Debug --no-restore --nologo

# ── Test ──────────────────────────────────────────────────

# Run unit tests (no Temporal server required)
test-unit: build
    dotnet test "{{unit_tests_dir}}" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --logger "trx;LogFileName=unit.trx"
    dotnet test "{{general_analyzer_tests_dir}}" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --logger "trx;LogFileName=analyzers.trx"
    dotnet test "{{durable_analyzer_tests_dir}}" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --logger "trx;LogFileName=durable-analyzers.trx"
    dotnet test "{{generated_client_tests_dir}}" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --logger "trx;LogFileName=generated-clients.trx"
    dotnet test "{{general_codefix_tests_dir}}" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --logger "trx;LogFileName=analyzer-codefixes.trx"
    dotnet test "{{durable_codefix_tests_dir}}" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --logger "trx;LogFileName=durable-analyzer-codefixes.trx"
    # MSBUILDDISABLENODEREUSE=1: these tests spawn many real "dotnet new"/"dotnet build"
    # subprocesses; MSBuild's default node-reuse workers can outlive the subprocess that spawned
    # them and keep its redirected stdout/stderr pipe open, which hangs
    # Process.StandardOutput.ReadToEndAsync() indefinitely — confirmed empirically (a 15+ minute
    # hang that resolved to a normal ~30s run once node reuse was disabled).
    MSBUILDDISABLENODEREUSE=1 dotnet test "{{template_tests_dir}}" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --logger "trx;LogFileName=templates.trx"

# Run integration tests (uses WorkflowEnvironment.StartLocalAsync — no external server needed)
test-integration: build
    dotnet test "{{integration_tests_dir}}" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --logger "trx;LogFileName=integration.trx"

# Run all tests (unit + integration)
test: test-unit test-integration

# Run tests matching a filter expression
# Example: just test-filter "FullyQualifiedName~ScenarioA"
test-filter FILTER: build
    dotnet test "{{solution}}" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --filter "{{FILTER}}"

# Run unit tests with code coverage (Coverlet XPlat)
test-coverage: build
    dotnet test "{{unit_tests_dir}}" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --collect "XPlat Code Coverage" \
        --results-directory "{{coverage_dir}}"

# Generate HTML coverage report from last test-coverage run
coverage-report:
    dotnet tool run reportgenerator \
        -reports:"{{coverage_dir}}/**/coverage.cobertura.xml" \
        -targetdir:"{{coverage_dir}}/report" \
        -reporttypes:Html

# [unix] Run a single named test with wall-clock timeout (hang detection)
# Example: just test-logged "ScenarioG_CanonicalObjectReminder"
[unix]
test-logged TEST: build
    #!/usr/bin/env bash
    set -euo pipefail
    log="{{coverage_dir}}/{{TEST}}-$(date +%Y%m%d%H%M%S).log"
    mkdir -p "{{coverage_dir}}"
    echo "Running {{TEST}} — log: $log"
    if ! command -v timeout >/dev/null 2>&1; then
        echo "ERROR: GNU coreutils 'timeout' required. On macOS: brew install coreutils"
        exit 1
    fi
    timeout 120 dotnet test "{{integration_tests_dir}}" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --filter "FullyQualifiedName~{{TEST}}" \
        --logger "console;verbosity=detailed" \
        2>&1 | tee "$log" \
        && echo "✓ PASS" || echo "✗ FAIL — see $log"

# [unix] Run each integration scenario individually to isolate failures
[unix]
test-individual: build
    #!/usr/bin/env bash
    set -euo pipefail
    failed=()
    scenarios=( A B C D E F G H I J K L M N O P )
    for s in "${scenarios[@]}"; do
        echo "── Scenario $s ──"
        if dotnet test "{{integration_tests_dir}}" \
            --configuration "{{configuration}}" \
            --no-build \
            --nologo \
            --filter "FullyQualifiedName~Scenario${s}_" \
            --logger "console;verbosity=normal" 2>&1; then
            echo "✓ Scenario $s PASS"
        else
            echo "✗ Scenario $s FAIL"
            failed+=("$s")
        fi
    done
    if [ ${#failed[@]} -gt 0 ]; then
        echo "Failed scenarios: ${failed[*]}"
        exit 1
    fi

# ── Benchmarks ────────────────────────────────────────────

# Run BenchmarkDotNet benchmarks (NOT in CI — requires Release build and quiet machine)
bench:
    dotnet run --project "{{benchmarks_dir}}" --configuration Release -- --filter "*"

# ── Pack + Publish ─────────────────────────────────────────

# Pack the library into .nupkg + .snupkg (MinVer derives version from git tags)
pack: build
    mkdir -p "{{artifacts_dir}}"
    dotnet pack "src/TemporalCommunity.DurableObjects" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --output "{{artifacts_dir}}"
    dotnet pack "src/TemporalCommunity.Extensions.Analyzers" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --output "{{artifacts_dir}}"
    dotnet pack "src/TemporalCommunity.DurableObjects.Analyzers" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --output "{{artifacts_dir}}"
    dotnet pack "templates/TemporalCommunity.Templates" \
        --configuration "{{configuration}}" \
        --no-build \
        --nologo \
        --output "{{artifacts_dir}}"

# Verify the packed nupkg: confirm net10.0 + net8.0 + netstandard2.1 lib/ folders exist,
# then compile a netstandard2.1 consumer project against the local package.
[unix]
pack-verify: pack
    #!/usr/bin/env bash
    set -euo pipefail
    pkg=$(ls "{{artifacts_dir}}"/TemporalCommunity.DurableObjects.{{version}}.nupkg 2>/dev/null | head -1)
    [ -n "$pkg" ] || { echo "ERROR: no .nupkg found in {{artifacts_dir}}"; exit 1; }
    echo "==> Checking lib/ folders in $(basename "$pkg")"
    for tfm in net10.0 net8.0 netstandard2.1; do
        if unzip -Z1 "$pkg" | grep -Fx "lib/$tfm/TemporalCommunity.DurableObjects.dll" >/dev/null; then
            echo "  ✓ lib/$tfm/ present"
        else
            echo "  ✗ ERROR: lib/$tfm/ missing from nupkg" >&2; exit 1
        fi
    done
    echo "==> Checking analyzer package assets"
    general_analyzer_pkg="{{artifacts_dir}}/TemporalCommunity.Extensions.Analyzers.{{version}}.nupkg"
    durable_analyzer_pkg="{{artifacts_dir}}/TemporalCommunity.DurableObjects.Analyzers.{{version}}.nupkg"
    unzip -Z1 "$general_analyzer_pkg" | grep -Fx 'analyzers/dotnet/cs/TemporalCommunity.Extensions.Analyzers.dll' >/dev/null
    unzip -Z1 "$general_analyzer_pkg" | grep -Fx 'analyzers/dotnet/cs/TemporalCommunity.Extensions.Analyzers.CodeFixes.dll' >/dev/null
    unzip -Z1 "$durable_analyzer_pkg" | grep -Fx 'analyzers/dotnet/cs/TemporalCommunity.DurableObjects.Analyzers.dll' >/dev/null
    unzip -Z1 "$durable_analyzer_pkg" | grep -Fx 'analyzers/dotnet/cs/TemporalCommunity.DurableObjects.Analyzers.CodeFixes.dll' >/dev/null
    echo "  ✓ analyzer and code-fix assets present in both packages"
    echo "==> Consumer compilation test (netstandard2.1)"
    consumer_dir=$(mktemp -d /tmp/ns21-consumer.XXXXXX)
    analyzer_consumer_dir=$(mktemp -d /tmp/analyzer-consumer.XXXXXX)
    consumer_packages=$(mktemp -d /tmp/ns21-packages.XXXXXX)
    templates_hive=$(mktemp -d /tmp/templates-hive.XXXXXX)
    templates_scratch=$(mktemp -d /tmp/templates-scratch.XXXXXX)
    templates_worker_scratch=$(mktemp -d /tmp/templates-worker-scratch.XXXXXX)
    templates_solution_scratch=$(mktemp -d /tmp/templates-solution-scratch.XXXXXX)
    # Single trap covering every scratch dir created in this recipe — a later `trap ... EXIT`
    # would replace this one rather than stack with it, leaking whichever dirs were registered
    # first.
    trap 'rm -rf "$consumer_dir" "$analyzer_consumer_dir" "$consumer_packages" "$templates_hive" "$templates_scratch" "$templates_worker_scratch" "$templates_solution_scratch"' EXIT
    local_source=$(realpath "{{artifacts_dir}}")
    echo "==> Packed analyzer consumer test"
    printf '%s\n' \
        '<Project Sdk="Microsoft.NET.Sdk">' \
        '  <PropertyGroup>' \
        '    <TargetFramework>net10.0</TargetFramework>' \
        '    <Nullable>enable</Nullable>' \
        '    <ImplicitUsings>enable</ImplicitUsings>' \
        '    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>' \
        '    <NoWarn>CA1050;CA1822;CA2007;CS1591</NoWarn>' \
        '  </PropertyGroup>' \
        '  <ItemGroup>' \
        "    <PackageReference Include=\"TemporalCommunity.DurableObjects\" Version=\"{{version}}\" />" \
        "    <PackageReference Include=\"TemporalCommunity.Extensions.Analyzers\" Version=\"{{version}}\" PrivateAssets=\"all\" />" \
        "    <PackageReference Include=\"TemporalCommunity.DurableObjects.Analyzers\" Version=\"{{version}}\" PrivateAssets=\"all\" />" \
        '  </ItemGroup>' \
        '</Project>' \
        > "$analyzer_consumer_dir/analyzer-consumer.csproj"
    printf '%s\n' \
        'using Temporalio.Workflows;' \
        'using TemporalCommunity.DurableObjects;' \
        '' \
        '[Workflow]' \
        'public sealed class InvalidWorkflow' \
        '{' \
        '    [WorkflowRun]' \
        '    public async Task RunAsync() => await Task.Delay(1);' \
        '}' \
        '' \
        'public interface IInvalidObject : IDurableObject' \
        '{' \
        '    Task IncrementAsync();' \
        '}' \
        > "$analyzer_consumer_dir/Consumer.cs"
    if NUGET_PACKAGES="$consumer_packages" dotnet build "$analyzer_consumer_dir/analyzer-consumer.csproj" \
        --nologo \
        -p:RestoreAdditionalProjectSources="$local_source" \
        > "$analyzer_consumer_dir/invalid.log" 2>&1; then
        echo "  ✗ ERROR: invalid analyzer consumer unexpectedly compiled" >&2
        cat "$analyzer_consumer_dir/invalid.log"
        exit 1
    fi
    grep -q 'TEMP002' "$analyzer_consumer_dir/invalid.log"
    grep -q 'DO0001' "$analyzer_consumer_dir/invalid.log"
    if grep -q 'CS8032' "$analyzer_consumer_dir/invalid.log"; then
        echo "  ✗ ERROR: compiler could not load a packaged analyzer assembly" >&2
        cat "$analyzer_consumer_dir/invalid.log"
        exit 1
    fi
    echo "  ✓ packed analyzers produced TEMP002 and DO0001 through dotnet build"
    printf '%s\n' \
        'using Temporalio.Workflows;' \
        'using TemporalCommunity.DurableObjects;' \
        '' \
        '[Workflow]' \
        'public sealed class ValidWorkflow' \
        '{' \
        '    [WorkflowRun]' \
        '    public async Task RunAsync() => await Workflow.DelayAsync(TimeSpan.FromMilliseconds(1));' \
        '}' \
        '' \
        'public interface IValidObject : IDurableObject' \
        '{' \
        '    [WorkflowUpdate] Task IncrementAsync();' \
        '}' \
        '' \
        'public static class GeneratedClientCheck' \
        '{' \
        '    public static Type ClientType => typeof(ValidObjectDurableObjectClient);' \
        '}' \
        > "$analyzer_consumer_dir/Consumer.cs"
    NUGET_PACKAGES="$consumer_packages" dotnet build "$analyzer_consumer_dir/analyzer-consumer.csproj" \
        --nologo \
        --no-restore \
        -p:RestoreAdditionalProjectSources="$local_source"
    echo "  ✓ valid packed-analyzer consumer compiled and referenced its generated client"
    # printf avoids a heredoc whose body would start with '<' at column 1 — just's parser
    # treats '<' at column 1 as an unknown token and rejects the recipe before it runs.
    printf '%s\n' \
        '<Project Sdk="Microsoft.NET.Sdk">' \
        '  <PropertyGroup>' \
        '    <TargetFramework>netstandard2.1</TargetFramework>' \
        '    <Nullable>enable</Nullable>' \
        '    <LangVersion>latest</LangVersion>' \
        '  </PropertyGroup>' \
        '  <ItemGroup>' \
        "    <PackageReference Include=\"TemporalCommunity.DurableObjects\" Version=\"{{version}}\" />" \
        '  </ItemGroup>' \
        '</Project>' \
        > "$consumer_dir/consumer.csproj"
    printf '%s\n' \
        'using System;' \
        'using TemporalCommunity.DurableObjects;' \
        '' \
        'namespace Consumer;' \
        '' \
        'public static class ApiCheck' \
        '{' \
        '    public static Type DurableObjectBaseType => typeof(DurableObjectBase);' \
        '    public static Type DurableObjectFactoryType => typeof(IDurableObjectFactory);' \
        '    public static System.Collections.Generic.IAsyncEnumerable<string> List(IDurableObjectFactory factory) =>' \
        '        factory.ListDurableObjectsAsync<IDurableObject>();' \
        '    public static System.Collections.Generic.IAsyncEnumerable<DurableObjectExecutionInfo> ListRich(IDurableObjectFactory factory) =>' \
        '        factory.ListDurableObjectExecutionsAsync<IDurableObject>();' \
        '    public static bool AllowsReminders =>' \
        '        DurableObjectWorkerInterceptor.FrameworkUpdateNames.Contains("OnReminder");' \
        '}' \
        > "$consumer_dir/Consumer.cs"
    NUGET_PACKAGES="$consumer_packages" dotnet build "$consumer_dir/consumer.csproj" \
        --nologo \
        -p:RestoreAdditionalProjectSources="$local_source"
    echo "  ✓ netstandard2.1 consumer compiled successfully"
    echo "==> Consumer asset-selection test (net8.0)"
    NUGET_PACKAGES="$consumer_packages" dotnet restore "$consumer_dir/consumer.csproj" \
        --nologo \
        -p:TargetFramework=net8.0 \
        -p:RestoreForce=true \
        -p:RestoreAdditionalProjectSources="$local_source"
    NUGET_PACKAGES="$consumer_packages" dotnet build "$consumer_dir/consumer.csproj" \
        --nologo \
        --no-restore \
        -p:TargetFramework=net8.0 \
        -p:RestoreAdditionalProjectSources="$local_source"
    grep -q 'lib/net8.0/TemporalCommunity.DurableObjects.dll' "$consumer_dir/obj/project.assets.json"
    echo "  ✓ net8.0 consumer selected the net8.0 package asset"
    echo "==> Templates package installation test (isolated hive)"
    templates_pkg="{{artifacts_dir}}/TemporalCommunity.Templates.{{version}}.nupkg"
    [ -f "$templates_pkg" ] || { echo "  ✗ ERROR: no templates nupkg found at $templates_pkg" >&2; exit 1; }
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalWorkflow/.template.config/template.json' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalWorkflow/TemporalWorkflow1.cs' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalActivity/.template.config/template.json' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalActivity/TemporalActivity1.cs' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalPayloadConverter/.template.config/template.json' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalPayloadConverter/TemporalPayloadConverter1.cs' >/dev/null
    echo "  ✓ content/TemporalWorkflow/**, content/TemporalActivity/**, and content/TemporalPayloadConverter/** present in nupkg"
    # --nuget-source only selects a package source, not a hive location — point DOTNET_CLI_HOME
    # at a scratch directory so this install/instantiate round-trip never touches the real
    # user-wide template hive. Note: "dotnet new" subcommands (install/instantiate) reject
    # --nologo outright (confirmed empirically: exit 127, "'--nologo' is not a valid option") —
    # unlike build/restore/pack, it is not a recognized option there. DOTNET_NOLOGO/
    # DOTNET_CLI_TELEMETRY_OPTOUT instead suppress the first-run welcome banner this fresh,
    # isolated CLI home would otherwise print.
    export DOTNET_NOLOGO=1
    export DOTNET_CLI_TELEMETRY_OPTOUT=1
    DOTNET_CLI_HOME="$templates_hive" dotnet new install "$templates_pkg"
    echo "  ✓ TemporalCommunity.Templates installed into isolated hive"
    DOTNET_CLI_HOME="$templates_hive" dotnet new classlib -n ScratchTemplateHost -o "$templates_scratch" --no-restore
    (cd "$templates_scratch" && DOTNET_CLI_HOME="$templates_hive" dotnet restore)
    (cd "$templates_scratch" && DOTNET_CLI_HOME="$templates_hive" dotnet new temporal-workflow -n DryRunWorkflow -o . --dry-run)
    echo "  ✓ temporal-workflow --dry-run reported without error"
    (cd "$templates_scratch" && DOTNET_CLI_HOME="$templates_hive" dotnet new temporal-workflow -n SampleWorkflow -o .)
    [ -f "$templates_scratch/SampleWorkflow.cs" ] || { echo "  ✗ ERROR: temporal-workflow did not generate SampleWorkflow.cs" >&2; exit 1; }
    (cd "$templates_scratch" && DOTNET_CLI_HOME="$templates_hive" dotnet new temporal-activity -n DryRunActivity -o . --dry-run)
    echo "  ✓ temporal-activity --dry-run reported without error"
    (cd "$templates_scratch" && DOTNET_CLI_HOME="$templates_hive" dotnet new temporal-activity -n SampleActivity -o .)
    [ -f "$templates_scratch/SampleActivity.cs" ] || { echo "  ✗ ERROR: temporal-activity did not generate SampleActivity.cs" >&2; exit 1; }
    (cd "$templates_scratch" && DOTNET_CLI_HOME="$templates_hive" dotnet new temporal-payload-converter -n DryRunPayloadConverter -o . --dry-run)
    echo "  ✓ temporal-payload-converter --dry-run reported without error"
    (cd "$templates_scratch" && DOTNET_CLI_HOME="$templates_hive" dotnet new temporal-payload-converter -n SamplePayloadConverter -o .)
    [ -f "$templates_scratch/SamplePayloadConverter.cs" ] || { echo "  ✗ ERROR: temporal-payload-converter did not generate SamplePayloadConverter.cs" >&2; exit 1; }
    (cd "$templates_scratch" && dotnet add package Temporalio --version 1.16.0)
    (cd "$templates_scratch" && dotnet build --nologo)
    echo "  ✓ real temporal-workflow, temporal-activity, and temporal-payload-converter instantiations compiled inside a scratch project"
    echo "==> temporal-worker project template checks"
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalWorker/.template.config/template.json' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalWorker/.template.config/dotnetcli.host.json' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalWorker/TemporalWorker1.csproj' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalWorker/Program.cs' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalWorker/TemporalConnection.cs' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalWorker/Workflows/SampleWorkflow.cs' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalWorker/Activities/SampleActivities.cs' >/dev/null
    echo "  ✓ content/TemporalWorker/** present in nupkg"
    mkdir -p "$templates_worker_scratch/dry-run"
    (cd "$templates_worker_scratch/dry-run" && DOTNET_CLI_HOME="$templates_hive" dotnet new temporal-worker -n DryRunWorker -o . --dry-run)
    echo "  ✓ temporal-worker --dry-run reported without error"
    # Full Framework x IncludeOtel 2x2 matrix — a project template, unlike the item templates
    # above, needs no ScratchTemplateHost since it generates its own standalone .csproj.
    for fw in net8.0 net10.0; do
        for otel in false true; do
            combo_dir="$templates_worker_scratch/${fw}-otel-${otel}"
            mkdir -p "$combo_dir"
            name="Worker_${fw//./}_${otel}"
            (cd "$combo_dir" && DOTNET_CLI_HOME="$templates_hive" dotnet new temporal-worker -n "$name" -o . --framework "$fw" --include-otel "$otel")
            [ -f "$combo_dir/$name.csproj" ] || { echo "  ✗ ERROR: temporal-worker (framework=$fw, include-otel=$otel) did not generate $name.csproj" >&2; exit 1; }
            (cd "$combo_dir" && dotnet build --nologo)
            echo "  ✓ temporal-worker (framework=$fw, include-otel=$otel) instantiated and built standalone"
        done
    done
    echo "==> temporal-solution multi-project template checks"
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalSolution/.template.config/template.json' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalSolution/.template.config/dotnetcli.host.json' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalSolution/TemporalSolution.1.sln' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalSolution/TemporalSolution.1.Worker/TemporalSolution.1.Worker.csproj' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalSolution/TemporalSolution.1.Client/TemporalSolution.1.Client.csproj' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalSolution/TemporalSolution.1.Shared/TemporalSolution.1.Shared.csproj' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalSolution/TemporalSolution.1.Shared/SharedTemporalConnection.cs' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalSolution/TemporalSolution.1.AppHost/AppHost.cs' >/dev/null
    unzip -Z1 "$templates_pkg" | grep -Fx 'content/TemporalSolution/TemporalSolution.1.ServiceDefaults/Extensions.cs' >/dev/null
    echo "  ✓ content/TemporalSolution/** present in nupkg"
    mkdir -p "$templates_solution_scratch/dry-run"
    (cd "$templates_solution_scratch/dry-run" && DOTNET_CLI_HOME="$templates_hive" dotnet new temporal-solution -n DryRunSolution -o . --dry-run)
    echo "  ✓ temporal-solution --dry-run reported without error"
    # Full Framework x IncludeAspire x IncludeOtel matrix, built via the generated .sln (not just
    # individual projects) to catch broken ProjectReference paths from sourceName substitution.
    for fw in net8.0 net10.0; do
        for aspire in false true; do
            for otel in false true; do
                combo_dir="$templates_solution_scratch/${fw}-aspire-${aspire}-otel-${otel}"
                mkdir -p "$combo_dir"
                name="Sol_${fw//./}_${aspire}_${otel}"
                (cd "$combo_dir" && DOTNET_CLI_HOME="$templates_hive" dotnet new temporal-solution -n "$name" -o . --framework "$fw" --include-aspire "$aspire" --include-otel "$otel")
                [ -f "$combo_dir/$name.sln" ] || { echo "  ✗ ERROR: temporal-solution (framework=$fw, include-aspire=$aspire, include-otel=$otel) did not generate $name.sln" >&2; exit 1; }
                if [ "$aspire" = "true" ]; then
                    [ -d "$combo_dir/$name.AppHost" ] || { echo "  ✗ ERROR: expected $name.AppHost with include-aspire=true" >&2; exit 1; }
                else
                    [ -d "$combo_dir/$name.AppHost" ] && { echo "  ✗ ERROR: did not expect $name.AppHost with include-aspire=false" >&2; exit 1; }
                fi
                (cd "$combo_dir" && dotnet build "$name.sln" --nologo)
                echo "  ✓ temporal-solution (framework=$fw, include-aspire=$aspire, include-otel=$otel) instantiated and built"
            done
        done
    done
    echo "==> temporal-solution XML-sensitive name check"
    xml_name_dir="$templates_solution_scratch/xml-name"
    mkdir -p "$xml_name_dir"
    (cd "$xml_name_dir" && DOTNET_CLI_HOME="$templates_hive" dotnet new temporal-solution -n "Contoso-Fulfillment&Orders" -o . --include-aspire)
    grep -q 'Projects.Contoso_Fulfillment_Orders_Worker' "$xml_name_dir/Contoso-Fulfillment&Orders.AppHost/AppHost.cs"
    grep -q 'Contoso-Fulfillment&amp;Orders.Shared' "$xml_name_dir/Contoso-Fulfillment&Orders.Worker/Contoso-Fulfillment&Orders.Worker.csproj"
    (cd "$xml_name_dir" && dotnet build "Contoso-Fulfillment&Orders.sln" --nologo)
    echo "  ✓ XML-sensitive project name sanitized correctly and built"

# ── Template runtime smoke tests ────────────────────────────
# Two separate recipes, each owning its own cleanup trap — a later `trap ... EXIT` within the same
# shell replaces an earlier one rather than stacking with it (confirmed empirically), so combining
# both branches into one recipe would let the second branch's trap silently drop the first
# branch's cleanup on failure. Aggregated by `template-smoke-test` below.

# [unix] Standalone (IncludeAspire=false) runtime smoke test: real temporal server + Worker + Client.
[unix]
template-smoke-test-standalone: pack
    #!/usr/bin/env bash
    set -euo pipefail
    SERVER_PID=
    WORKER_PID=
    SCRATCH_DIR=
    HIVE_DIR=
    cleanup() {
        # Confirmed empirically: `dotnet run` starts the compiled apphost executable as a *child*
        # process rather than exec-replacing itself, so `kill "$WORKER_PID"` only kills the
        # "dotnet run" wrapper and leaves the actual Worker process running indefinitely. Matching
        # on $SCRATCH_DIR (a unique per-run temp path) catches both the wrapper and its child.
        if [ -n "$SCRATCH_DIR" ]; then pkill -f "$SCRATCH_DIR" 2>/dev/null || true; fi
        if [ -n "$WORKER_PID" ]; then kill "$WORKER_PID" 2>/dev/null || true; wait "$WORKER_PID" 2>/dev/null || true; fi
        if [ -n "$SERVER_PID" ]; then kill "$SERVER_PID" 2>/dev/null || true; wait "$SERVER_PID" 2>/dev/null || true; fi
        if [ -n "$SCRATCH_DIR" ]; then rm -rf "$SCRATCH_DIR"; fi
        if [ -n "$HIVE_DIR" ]; then rm -rf "$HIVE_DIR"; fi
    }
    trap cleanup EXIT

    echo "==> Starting temporal server"
    temporal server start-dev >/tmp/template-smoke-server.log 2>&1 &
    SERVER_PID=$!
    ready=false
    for _ in $(seq 1 30); do
        if temporal operator cluster health >/dev/null 2>&1; then
            ready=true
            break
        fi
        sleep 1
    done
    [ "$ready" = true ] || { echo "  ✗ ERROR: temporal server did not become ready" >&2; cat /tmp/template-smoke-server.log >&2; exit 1; }
    echo "  ✓ temporal server ready"

    # realpath: on macOS /tmp is a symlink to /private/tmp — resolving it once here keeps every
    # later reference (this script's own and any project-to-project paths dotnet new/MSBuild
    # record internally) consistently spelled, avoiding NuGet/MSBuild treating the same file as
    # two different projects when reached through different-looking (but identical) paths.
    SCRATCH_DIR=$(realpath "$(mktemp -d /tmp/template-smoke-standalone.XXXXXX)")
    HIVE_DIR=$(realpath "$(mktemp -d /tmp/template-smoke-standalone-hive.XXXXXX)")
    export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
    DOTNET_CLI_HOME="$HIVE_DIR" dotnet new install "{{artifacts_dir}}/TemporalCommunity.Templates.{{version}}.nupkg"
    (cd "$SCRATCH_DIR" && DOTNET_CLI_HOME="$HIVE_DIR" dotnet new temporal-solution -n SmokeStandalone -o .)

    echo "==> Starting worker"
    (cd "$SCRATCH_DIR/SmokeStandalone.Worker" && dotnet run --nologo) >/tmp/template-smoke-worker.log 2>&1 &
    WORKER_PID=$!
    sleep 5

    echo "==> Running client"
    (cd "$SCRATCH_DIR/SmokeStandalone.Client" && dotnet run --nologo) | tee /tmp/template-smoke-client.log
    grep -q 'Workflow result: Hello, world!' /tmp/template-smoke-client.log || {
        echo "  ✗ ERROR: client did not report the expected workflow result" >&2
        echo "worker log:" >&2; cat /tmp/template-smoke-worker.log >&2
        exit 1
    }
    echo "  ✓ standalone smoke test: client received the expected workflow result"

# [unix] Aspire (IncludeAspire=true) runtime smoke test: AppHost auto-provisions the dev server.
[unix]
template-smoke-test-aspire: pack
    #!/usr/bin/env bash
    set -euo pipefail
    APPHOST=
    SCRATCH_DIR=
    HIVE_DIR=
    cleanup() {
        if [ -n "$APPHOST" ]; then aspire stop --apphost "$APPHOST" >/dev/null 2>&1 || true; fi
        if [ -n "$SCRATCH_DIR" ]; then rm -rf "$SCRATCH_DIR"; fi
        if [ -n "$HIVE_DIR" ]; then rm -rf "$HIVE_DIR"; fi
    }
    trap cleanup EXIT

    # realpath: see the standalone recipe's comment — avoids a real, empirically-confirmed NuGet
    # restore collision ("project.assets.json already exists") from /tmp vs. /private/tmp path
    # spelling inconsistency on macOS.
    SCRATCH_DIR=$(realpath "$(mktemp -d /tmp/template-smoke-aspire.XXXXXX)")
    HIVE_DIR=$(realpath "$(mktemp -d /tmp/template-smoke-aspire-hive.XXXXXX)")
    export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
    DOTNET_CLI_HOME="$HIVE_DIR" dotnet new install "{{artifacts_dir}}/TemporalCommunity.Templates.{{version}}.nupkg"
    (cd "$SCRATCH_DIR" && DOTNET_CLI_HOME="$HIVE_DIR" dotnet new temporal-solution -n SmokeAspire -o . --include-aspire)

    apphost_candidates=$(find "$SCRATCH_DIR" -maxdepth 2 -name '*.AppHost.csproj')
    if [ "$(printf '%s\n' "$apphost_candidates" | wc -l)" -ne 1 ] || [ -z "$apphost_candidates" ]; then
        echo "  ✗ ERROR: expected exactly one *.AppHost.csproj, found: $apphost_candidates" >&2
        exit 1
    fi
    APPHOST="$apphost_candidates"

    # Required — confirmed empirically. `aspire start` launches each project resource via
    # `dotnet run --no-build`, which assumes a prior build already produced a complete output
    # (including transitive ProjectReference dependencies like ServiceDefaults) for every project;
    # it does not build them itself. Without this, Client failed at runtime with
    # "Could not load file or assembly 'SmokeAspire.ServiceDefaults'" even though restore succeeded.
    solution_file=$(find "$SCRATCH_DIR" -maxdepth 1 -name '*.sln')
    dotnet build "$solution_file" --nologo -m:1
    aspire start --apphost "$APPHOST" --non-interactive
    aspire wait temporal --apphost "$APPHOST" --status healthy
    aspire wait worker --apphost "$APPHOST" --status up
    aspire wait client --apphost "$APPHOST" --status down
    aspire logs client --apphost "$APPHOST" --format Json > "$SCRATCH_DIR/client-logs.json"
    grep -q 'Workflow result: Hello, world!' "$SCRATCH_DIR/client-logs.json" || {
        echo "  ✗ ERROR: client logs did not contain the expected workflow result" >&2
        cat "$SCRATCH_DIR/client-logs.json" >&2
        exit 1
    }
    echo "  ✓ aspire smoke test: client logs contain the expected workflow result"

# Runs both runtime smoke-test branches (each owns its own cleanup — see the comment above).
template-smoke-test: template-smoke-test-standalone template-smoke-test-aspire

# Push to NuGet.org (NUGET_API_KEY required; CI uses OIDC Trusted Publishing instead)
publish-nuget: pack
    dotnet nuget push "{{artifacts_dir}}/*.nupkg" \
        --source "https://api.nuget.org/v3/index.json" \
        --api-key "$NUGET_API_KEY" \
        --skip-duplicate

# Push to all configured git remotes (with tags)
sync-remotes:
    git remote | xargs -I{} git push {} --follow-tags

# ── Sample ────────────────────────────────────────────────

# Run a sample project (requires a running Temporal server at localhost:7233)
# Usage: just run-sample           → runs 01-getting-started
#        just run-sample 03        → runs 03-scheduling
#        just run-sample-all       → lists all available samples
run-sample SAMPLE="01-getting-started":
    dotnet run --project "samples/{{SAMPLE}}" --configuration "{{configuration}}"

# List all available samples
run-sample-all:
    @echo "Available samples:"
    @echo "  01-getting-started   — Full setup + client proxy"
    @echo "  02-input-validation  — Update validators + error handling"
    @echo "  03-scheduling        — Schedule vs Reminder patterns"
    @echo "  04-object-to-object  — Activity-mediated DO-to-DO"
    @echo "  05-observability     — OpenTelemetry traces"
    @echo "  06-testing           — xUnit reference test suite (use: dotnet test samples/06-testing)"

# ── Process Hygiene (Unix only) ────────────────────────────
# These recipes use Unix utilities (pkill, pgrep, find) and do not run on Windows.
# On Windows: use Task Manager or Stop-Process in PowerShell directly.

# [unix] List orphaned temporal-sdk-dotnet processes from failed integration tests
[unix]
list-orphans:
    @echo "== temporal-sdk-dotnet processes =="
    @pgrep -af "temporal-sdk-dotnet" 2>/dev/null || echo "(none)"

# [unix] Kill orphaned temporal-sdk-dotnet processes (SIGTERM then SIGKILL)
[unix]
kill-orphans:
    @echo "Sending SIGTERM to orphaned temporal-sdk-dotnet processes..."
    -@pkill -TERM -f "[t]emporal-sdk-dotnet" 2>/dev/null; true
    @echo "Sending SIGKILL to any stragglers..."
    -@pkill -9 -f "[t]emporal-sdk-dotnet" 2>/dev/null; true
    @pgrep -af "[t]emporal-sdk-dotnet" 2>/dev/null || echo "(none remaining)"

# [unix] Kill stale dotnet test hosts scoped to this repo
[unix]
kill-test-hosts:
    @echo "== dotnet test processes for this repo =="
    @pgrep -af "dotnet test" 2>/dev/null | grep -i "DurableObjects" | grep -v "just " || echo "(none)"
    -@pkill -f "dotnet.*testhost.*DurableObjects" 2>/dev/null; true

# [unix] Full cleanup: kill test hosts + kill orphans + remove stale test artifacts
[unix]
test-clean: kill-test-hosts kill-orphans clean-tests

# ── Aliases ───────────────────────────────────────────────

alias compile := build
alias verify  := test
alias validate := test-unit

# CI pipeline: clean → build → unit tests → pack (all pure dotnet, cross-platform)
ci: clean build test-unit pack-verify
