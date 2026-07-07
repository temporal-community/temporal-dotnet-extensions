# ADR 001 — .NET 10 Only for v1

**Status:** ACCEPTED

---

## Context

The library depends on the Temporal .NET SDK v1.16.0. During v1 development, several .NET 10
language and runtime features were used throughout the implementation:

- **Primary constructors** (C# 12) — `ReminderDeliveryActivities(ITemporalClient client)` and
  other types with constructor-injected dependencies.
- **Collection expressions** (C# 12) — `[dispatch.ReminderName, new ReminderDeliveryContext(id)]`
  in `DeliverReminderAsync`, interceptor array composition, and test fixtures.
- **`FrozenSet<T>`** (.NET 8+, used idiomatically on .NET 10) — `DurableObjectWorkerInterceptor.FrameworkUpdateNames`.
- **`ReadOnlySpan` params** and related runtime improvements used in test infrastructure.
- **`ArgumentOutOfRangeException.ThrowIfNegativeOrZero`** (.NET 8+ throw-helper) — `DurableObjectOptions` validation.

Multi-targeting (`net8.0`, `net9.0`, `net10.0`) would require conditional compilation,
polyfills or back-ports for these features, and a larger test matrix — before the API surface
has stabilized.

---

## Decision

Target `net10.0` only for v1. A single target framework simplifies the build, keeps csproj
files minimal, and allows the team to use the full .NET 10 feature set without guards.

---

## Rationale

1. **API stability first.** Multi-targeting is an operational commitment. Adding `net8.0` after
   the API stabilizes in v1.x is straightforward. Committing to multi-targeting before the API
   is stable means carrying backward-compatibility constraints before they are necessary.

2. **No known demand for net8/net9 at ship time.** The initial target audience is projects
   already running .NET 10, where Temporal SDK adoption is also newest. Backporting to LTS
   frameworks is planned but not urgent for v1.

3. **Avoids conditional compilation complexity.** Feature-detecting polyfills for
   `FrozenSet`, collection expressions, and throw-helpers adds noise to the source without
   adding value in v1.

---

## Consequences

- Callers must be on .NET 10. This is a hard requirement stated in the README and NuGet
  description.
- `net8.0` and `net9.0` support is planned for v1.x (likely v1.1) after the API surface has
  proven stable through real usage. The primary constructor and collection expression syntax
  will be replaced or conditionally compiled for lower TFMs at that point.
- The GitHub Actions CI matrix currently runs `net10.0` only; adding lower TFM runs is a
  mechanical change when multi-targeting is added.
