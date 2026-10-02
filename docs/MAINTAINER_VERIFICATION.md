# Maintainer Verification

The repository treats replay, scale, NativeAOT, and benchmarks as different signals. None of these
checks is a production capacity claim.

## Replay compatibility

`ReplayHistoryTests` replays the committed `rolling-counter.json` history on every integration-test
run. Preserve that fixture across implementation changes: a replay failure indicates that existing
executions may become non-deterministic. Add a new representative fixture instead of overwriting an
old one when a new workflow shape needs protection.

Run replay verification with:

```bash
dotnet test tests/TemporalCommunity.DurableObjects.IntegrationTests \
  --filter FullyQualifiedName~ReplayHistoryTests
```

## NativeAOT

`just aot-verify` publishes and executes the generated-client smoke application. It verifies that
the generated registry and concrete client survive NativeAOT compilation without `DispatchProxy`. It does
not claim NativeAOT support for reflection-based worker discovery or every Temporal SDK feature.

## Benchmarks and scale

`just bench` compares client creation and query dispatch for generated and compatibility clients using
BenchmarkDotNet. Run benchmarks on a quiet, dedicated machine and compare results from the same
hardware and runtime; the repository intentionally has no machine-dependent CI threshold.
Use [the load runner](../benchmarks/README.md) to exercise actual RPCs, history growth, and rollover
under traffic; the BenchmarkDotNet query transport is deliberately stubbed.

`ObjectIsolationTests` exercises 50 independent object IDs concurrently and asserts
state isolation. It is a bounded correctness regression test, not a throughput benchmark.

## Release packages

### Template packages and runtime smoke tests

Run `just pack-verify` on Linux or macOS before publishing a release. The recipe uses Unix/Bash
tooling; Windows maintainers can rely on the Ubuntu package-verification job in GitHub Actions. It
builds all four NuGet packages, checks their assets, and compiles representative consumers. The
NuGet publishing workflow publishes this package set together:

- `TemporalCommunity.DurableObjects`
- `TemporalCommunity.Extensions.Analyzers`
- `TemporalCommunity.DurableObjects.Analyzers`
- `TemporalCommunity.Templates`

The template pack lives in `templates/TemporalCommunity.Templates/`. Available commands:

```bash
just pack                             # packs the template nupkg into artifacts/packages
just pack-verify                      # isolated hive; instantiate and build template variants
just template-smoke-test-standalone   # real Temporal CLI dev server, Worker, and Client
just template-smoke-test-aspire       # real Aspire AppHost and provisioned Temporal dev server
just template-smoke-test              # sequentially run both runtime checks
```

The pack-verification item-template fixture deliberately compiles against Temporalio 1.16.0 as a
minimum-compatibility check; generated worker and solution projects pin Temporalio 1.20.0. This
fixture is not a generated-project dependency pin.

Additionally run `just template-smoke-test` before publishing a release that touches
`temporal-solution` — it exercises both the standalone (real `temporal server start-dev`) and
Aspire (real `aspire start`, auto-provisioned dev server) runtime paths against a real Worker and
Client, not just `dotnet build`. `pack-verify` alone only proves the generated code compiles.
The standalone branch binds its isolated dev server to `127.0.0.1:17233`, passes that address
explicitly to the health check, Worker, and Client, and verifies the server process remains alive.
Set `TEMPLATE_SMOKE_TEMPORAL_PORT` to use a different port when required.
Standalone smoke runs are one-at-a-time per host unless a coordinated strategy owns every
Temporal-bound port for each run; selecting a free port and releasing the probe is not sufficient
to make concurrent runs safe.

The standalone smoke recipe enforces a single invocation per host for its port with an atomic
`/tmp/temporal-template-smoke-17233.lock` (the lock name follows
`TEMPLATE_SMOKE_TEMPORAL_PORT`). A lock failure means another run owns the port; do not bypass it
with a probed "free" port. The recipe launches the built Worker DLL directly, tracks its PID,
uses per-run scratch/log directories, and waits up to 30 seconds for observable Worker readiness
before starting the Client. The
aggregate recipe runs standalone and Aspire sequentially; cleanup targets tracked processes
rather than broad process names. For the Aspire branch, build the generated solution before
`aspire start`: project resources launch via `dotnet run --no-build` and need all transitive
dependencies (including ServiceDefaults) in the output.

When dispatching `publish.yml`, choose the release type that matches the selected ref:

- `preview` may use an untagged branch or commit. It publishes the SemVer prerelease version
  calculated by MinVer, such as `0.3.5-preview.1` after `0.3.4`.
- `official` requires an exact stable SemVer tag, such as `0.3.5`. The workflow verifies that
  MinVer resolves to the same version.

The preview path rejects stable MinVer results, and the official path rejects branches, untagged
commits, prerelease tags, and mismatched MinVer versions before authenticating to NuGet.
