# Plan 005: Generated Asynchronous Clients

Status: Complete
Depends on: Plan 006
GitHub issue: [#8](https://github.com/temporal-community/temporal-dotnet-extensions/issues/8)
Completed by: [ff7738e](https://github.com/temporal-community/temporal-dotnet-extensions/commit/ff7738e)

## Outcome

Generate concrete, NativeAOT-compatible DurableObject clients with asynchronous queries and direct
update-with-start dispatch, while retaining `DispatchProxy` as a compatibility fallback during
migration.

## Proposed changes

- Define how workflow update/query contracts map to generated client methods.
- Generate concrete clients and a compile-time registry consumed by the factory.
- Prefer generated clients when registered; retain runtime proxy dispatch for existing contracts.
- Generate diagnostics for unsupported or ambiguous contract shapes.
- Prove parity, trimming behavior, and a real NativeAOT publish/run path before removing fallback.

## Compatibility

Existing `IDurableObjectFactory.Get<T>` calls and non-generated contracts must continue working.
Removing `DispatchProxy` requires a separate compatibility decision after adoption evidence.

## Implementation

- [x] Approve the asynchronous client and call-options API in ADR 009.
- [x] Specify generated source shape and registry integration.
- [x] Implement incremental generation and compile-output tests.
- [x] Add runtime parity tests against existing update/query behavior.
- [x] Add NativeAOT publish and execution verification.

## Tests

Test generated compilation, update/query behavior, cancellation and call options, contract errors,
fallback behavior, and NativeAOT. Avoid snapshot-only tests that do not compile generated output.

## Documentation

Update client usage, migration guidance, and one sample after the generated API is stable.

## Completion criteria

- Generated clients avoid `DispatchProxy` and reflection dispatch.
- Queries are asynchronous and call options are supported.
- Existing contracts retain a documented fallback.
- NativeAOT behavior is proven by an executable test application.
