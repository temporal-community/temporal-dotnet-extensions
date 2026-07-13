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
    scenarios=( A B C D E F G H I J K L M N O )
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
    trap 'rm -rf "$consumer_dir" "$analyzer_consumer_dir" "$consumer_packages"' EXIT
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
