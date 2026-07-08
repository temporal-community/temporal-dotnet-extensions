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
benchmarks_dir        := "benchmarks/TemporalCommunity.DurableObjects.Benchmarks"
sample_dir            := "samples/TemporalCommunity.DurableObjects.Sample"
version               := `dotnet tool run minver --default-pre-release-identifiers $(sed -n 's/.*<MinVerDefaultPreReleaseIdentifiers>\(.*\)<\/MinVerDefaultPreReleaseIdentifiers>.*/\1/p' Directory.Build.props | tr -d ' ')`

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
    -temporal workflow list --namespace default --limit 1 2>&1 || echo "⚠  Temporal server not reachable (required for integration tests)"
    @echo "==> minver-cli (local tool)"
    dotnet tool run minver --version

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

# Push to GitHub Packages (GITHUB_TOKEN required)
publish-github: pack
    dotnet nuget push "{{artifacts_dir}}/*.nupkg" \
        --source "https://nuget.pkg.github.com/$GITHUB_REPOSITORY_OWNER/index.json" \
        --api-key "$GITHUB_TOKEN" \
        --skip-duplicate

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

# Run the sample project (requires a running Temporal server at localhost:7233)
run-sample:
    dotnet run --project "{{sample_dir}}" --configuration "{{configuration}}"

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
ci: clean build test-unit pack
