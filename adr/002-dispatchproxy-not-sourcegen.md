# ADR 002 — DispatchProxy for v1; Source Generator Deferred to v1.1

**Status:** SUPERSEDED FOR GENERATED CLIENTS by [ADR 009](009-generated-asynchronous-clients.md)

> **The compatibility fallback is not NativeAOT-compatible.** `DispatchProxy` uses `Reflection.Emit`, which the AOT
> compiler trims. Publishing a DurableObject worker with `PublishAot=true` will fail at runtime
> with no compile-time warning. A source-generator-based proxy that eliminates this limitation
> is replaced for supported contracts by the generated client path in ADR 009.

---

## Context

`DurableObjectProxy<T>` is the internal mechanism that maps typed interface method calls (e.g.,
`counter.IncrementAsync()`) to Temporal RPC calls (`ExecuteUpdateWithStartWorkflowAsync`,
`QueryAsync`). Two implementation strategies were considered:

**`DispatchProxy` (reflection-based):** `DispatchProxy.Create<T, DurableObjectProxy<T>>()`
generates a class at runtime that implements `T` and overrides `Invoke(MethodInfo, object[]?)`.
The implementation boxes arguments, dispatches through `MethodInfo`, and routes to the
appropriate Temporal RPC. This is straightforward to implement correctly and has no build-time
tooling dependency.

**Source generator:** An incremental Roslyn source generator reads `[WorkflowUpdate]` and
`[WorkflowQuery]` attributes at compile time and emits a concrete implementing class per
interface. The generated class calls the RPC methods directly with typed arguments — no boxing,
no `MethodInfo`, no runtime code generation.

---

## Decision

Use `DispatchProxy` for v1. Source generation is deferred to a separate analyzer and generator
package (`TemporalCommunity.DurableObjects.Analyzers`).

---

## Rationale

### Performance difference is not the reason

The `DispatchProxy` reflection overhead is roughly 100–500 ns per dispatch (argument boxing,
`MethodInfo` dispatch, `MakeGenericMethod` for `Task<T>`). A source-generated concrete class
costs roughly 1–5 ns — a virtual method call. This difference is **irrelevant in practice**: the
cheapest Temporal operation (a local query) costs approximately 1 ms; a real-cluster round-trip
costs 10–100 ms. Reflection overhead is 3–5 orders of magnitude below the RPC cost. Choosing
`DispatchProxy` for v1 does not produce a measurably slower library.

### Why source generators are still the right long-term answer

Three reasons — none of them performance:

1. **NativeAOT compatibility.** `DispatchProxy` uses `Reflection.Emit`, which the .NET AOT
   compiler trims. Any consumer publishing with `PublishAot=true` gets a runtime
   `PlatformNotSupportedException` with no compile-time warning. This is a hard ceiling.

   **Failure symptom:** the exception is thrown at the moment `DispatchProxy.Create<T>()` is
   called — which happens inside `IDurableObjectFactory.Get<T>()` on first proxy creation for
   type `T`. The application will appear to start normally (the host starts, the worker
   registers) but the first call to `factory.Get<IMyObject>(id)` crashes with:

   ```
   System.PlatformNotSupportedException: Operation is not supported on this platform.
      at System.Reflection.DispatchProxy.Create[T,TProxy]()
      at TemporalCommunity.DurableObjects.DurableObjectProxy`1.CreateProxy(...)
   ```

   There is no compile-time warning. The failure only surfaces at runtime when the first typed
   factory call executes. Search for `PlatformNotSupportedException` at `DispatchProxy.Create`
   if you encounter this in a NativeAOT context.

2. **Compile-time rename safety.** `DispatchProxy` resolves RPC wire names at runtime from
   `MethodInfo`. A rename without a corresponding handler update produces a different wire name
   that fails at first integration test — not silently in production — but only if integration
   tests are run. A generator reads attributes at compile time and emits string literals; a
   rename produces a different literal that fails deterministically in tests.

3. **Static analysis.** A generated concrete class gives IDEs a full call graph,
   Go-to-Definition, and correct nullability flow. `DispatchProxy` is opaque to all static
   analysis tooling.

### Why `DispatchProxy` is acceptable for v1

- `DurableObjectProxy<T>` is `internal` — no external caller depends on it directly.
- The `MethodInfo` cache (static `ConcurrentDictionary<Type, MethodInfo>`) amortizes
  `MakeGenericMethod` cost across calls. **Cache scope:** the cache is a `static` field on the
  generic host class, making it per-`AppDomain` (per-process in modern .NET). It is shared
  across all `IDurableObjectFactory` instances in the same process. This is safe in production
  but has test-isolation implications: tests running in-process against multiple factory
  instances share the cache. Tests that mutate `MethodInfo` entries (if any) must account for
  cross-instance visibility. Tests in separate `AppDomain`-isolated processes (e.g., via
  `xunit` `[Collection]` isolation or a separate test project process) get a clean cache.
- `ValidateInterface<T>()` at `Get<T>()` time catches attribute mismatches at proxy creation,
  not at first RPC call, giving an earlier and clearer failure.
- The source generator requires Roslyn incremental generator infrastructure, parity tests, and
  AOT validation — non-trivial work that would delay v1 without user-visible benefit at current
  usage scale.

---

## Historical consequences and current status

- The original `DispatchProxy` path remains unavailable under NativeAOT.
- ADR 009 delivered `TemporalCommunity.DurableObjects.Analyzers`, concrete clients, parity tests,
  and NativeAOT validation in CI.
- The transition from `DispatchProxy` to generated proxies is internal to the library.
  The public `IDurableObjectFactory.Get<T>()` API surface does not change.
