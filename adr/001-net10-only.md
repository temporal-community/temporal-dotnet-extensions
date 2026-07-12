# ADR 001 — Target Frameworks

**Status:** Accepted

---

## Context

The library depends on the Temporal .NET SDK v1.16.0. During v1 development, several .NET 10
language and runtime features were used throughout the implementation:

- **Primary constructors** (C# 12) — `ReminderDeliveryActivities(ITemporalClient client)` and
  other types with constructor-injected dependencies.
- **Collection expressions** (C# 12) — `[dispatch.ReminderName, new ReminderDeliveryContext(id)]`
  in `DeliverReminderAsync`, interceptor array composition, and test fixtures.
- **`FrozenSet<T>`** (.NET 8+, available on `netstandard2.1` via `System.Collections.Immutable`) —
  `DurableObjectWorkerInterceptor.FrameworkUpdateNames`.
- **`ArgumentOutOfRangeException.ThrowIfNegativeOrZero`** (.NET 8+ throw-helper) — polyfilled via
  `Polyfills/ThrowHelpers.cs` for older targets.

---

## Decision

Ship `net10.0;net8.0;netstandard2.1` starting from v1. `net8.0` is the full-featured baseline
for supported .NET applications; `netstandard2.1` remains the broad-compatibility fallback.

---

## Rationale

The blockers for multi-targeting identified during v1 development were all resolved without API
or feature changes:

1. **`FrozenSet<T>`** — BCL-native on `net8.0` and `net10.0`; available on `netstandard2.1`
   via `System.Collections.Immutable` (the backport package). A conditional `PackageReference`
   adds it for the `netstandard2.1` TFM only.

2. **Throw-helpers** (`ArgumentNullException.ThrowIfNull` etc., .NET 7+) — centralized in
   `Polyfills/ThrowHelpers.cs`, which delegates to the BCL helpers on .NET 7+ and provides
   equivalent implementations on older targets. Call sites are unchanged.

3. **`init` setters and positional records** (require `IsExternalInit`, .NET 5+) — a one-line
   internal stub in `Polyfills/IsExternalInit.cs` satisfies the compiler requirement. No source
   changes at call sites.

4. **`IWorkerInterceptor.InterceptActivity` / `InterceptNexusOperation`** — the Temporal SDK
   gates these as default interface members (DIMs) behind `#if NETCOREAPP3_0_OR_GREATER`. On
   `netstandard2.1`, they are abstract. `DurableObjectWorkerInterceptor` adds pass-through
   implementations under the same `#if !NETCOREAPP3_0_OR_GREATER` guard, mirroring the SDK's
   own pattern.

5. **`ITemporalClient.ListWorkflowsAsync`** — gated by `#if NETCOREAPP3_0_OR_GREATER` in the
   SDK (`ITemporalClient.Workflow.cs`). `net8.0` compiles against the SDK asset that exposes the
   method, so `DurableObjectFactory.ListDurableObjectsAsync` works on .NET 8 and later.
   `netstandard2.1` retains a clear `PlatformNotSupportedException` branch. All other factory
   methods are fully supported on every target.

The public API surface is **identical** on all three TFMs. All assemblies expose the same types,
methods, and field types.

---

## Build infrastructure note

`Directory.Build.props` sets `<TargetFramework>net10.0</TargetFramework>` as a default for
test, sample, and benchmark projects that do not declare their own `TargetFrameworks`. Because
`Directory.Build.props` is loaded before project files, this value would be inherited by the
library csproj before it could set `<TargetFrameworks>`. To prevent MSBuild from using the
singular `TargetFramework` and silently skipping the multi-targeting loop, the library csproj
opens with `<TargetFramework />` to explicitly clear the inherited value before setting
`<TargetFrameworks>net10.0;net8.0;netstandard2.1</TargetFrameworks>`.

---

## Consequences

- .NET 8 and later consumers receive the `net8.0` asset and have full access to
  `ListDurableObjectsAsync`.
- `netstandard2.1` consumers can use all other library features, but
  `ListDurableObjectsAsync` throws `PlatformNotSupportedException`.
- .NET versions before 8 are out of support and receive the `netstandard2.1` fallback when it
  is compatible; the library does not promise visibility enumeration on those runtimes.
- The CI matrix executes tests on `net10.0`, while the library build and justfile `pack-verify`
  step cover all three package assets. The verification compiles a `netstandard2.1` consumer and
  confirms that a .NET 8 consumer selects the `net8.0` asset from the local package.
- Future TFM additions are straightforward — the polyfill infrastructure is already in place
  and the guards are additive.
