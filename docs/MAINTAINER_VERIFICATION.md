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

`just bench` compares generated-client creation with the compatibility proxy path using
BenchmarkDotNet. Run benchmarks on a quiet, dedicated machine and compare results from the same
hardware and runtime; the repository intentionally has no machine-dependent CI threshold.

`ScenarioP_ConcurrentObjectScale` exercises 50 independent object IDs concurrently and asserts
state isolation. It is a bounded correctness regression test, not a throughput benchmark.

## Release packages

Run `just pack-verify` before publishing a release. It builds all three NuGet packages, checks
their assets, and compiles representative consumers. The NuGet publishing workflow publishes this
package set together:

- `TemporalCommunity.DurableObjects`
- `TemporalCommunity.Extensions.Analyzers`
- `TemporalCommunity.DurableObjects.Analyzers`

When dispatching `publish.yml`, choose the release type that matches the selected ref:

- `preview` may use an untagged branch or commit. It publishes the SemVer prerelease version
  calculated by MinVer, such as `0.3.5-preview.1` after `0.3.4`.
- `official` requires an exact stable SemVer tag, such as `0.3.5`. The workflow verifies that
  MinVer resolves to the same version.

The preview path rejects stable MinVer results, and the official path rejects branches, untagged
commits, prerelease tags, and mismatched MinVer versions before authenticating to NuGet.
