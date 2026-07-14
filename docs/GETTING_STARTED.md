# Getting Started with DurableObjects

## Who is this library for?

This library is for .NET developers who are already using Temporal and want a higher-level,
object-oriented way to model stateful actors. Instead of writing workflow code directly — managing
run loops, update handlers, and history compaction yourself — you define an interface, implement
a class, and the library handles the Temporal plumbing. The mental model is a durable, always-
accessible object keyed by a stable ID, not a workflow with a defined start and end. If you are
not yet using Temporal, start with the [Temporal .NET SDK](https://github.com/temporalio/sdk-dotnet)
first and come back here when you want the object-oriented layer on top.

---

## Reading Order

### Just getting started (first time)

Work through this sequence top to bottom. Each step introduces one concept; none assumes the next.

1. **[Root `README.md`](../README.md) — Durable Objects minimal example** — The smallest complete
   path from contract definition through registration and a generated client call.
2. **[`docs/DURABLE_OBJECTS.md`](DURABLE_OBJECTS.md) — Concepts and API overview** — When to choose
   Durable Objects, identity, lifecycle, typed state, failure behavior, timers, generated clients,
   and the main factory operations.
3. **[`samples/01-getting-started`](../samples/01-getting-started/)** — The minimal example as a runnable project. Use this to verify
   your environment works before reading further.
4. **[`docs/BOILERPLATE.md`](BOILERPLATE.md) — Required patterns** — Four patterns every DurableObject must follow:
   `[WorkflowRun]` on every class, the `DeactivateAsync` override rule, the difference between
   `AddDurableObjects` and `AddDurableObjectWorkflows`, and why `ConfigureAwait(false)` is not
   optional. Read this once before writing your first real object.
5. **[`samples/02-input-validation`](../samples/02-input-validation/)** — Error handling and validators. Shows how to use
   `[WorkflowUpdateValidator]`, what `DurableObjectNotFoundException` and
   `DurableObjectNotActiveException` look like from the caller's side, and how to catch the right
   exception type from a failed update.
6. **[`docs/FAILURE_HANDLING.md`](FAILURE_HANDLING.md) — Introduction** — The exception mapping table and update handler
   failure taxonomy. After the first two samples, you will want to know why certain exceptions
   surface the way they do.

### Understanding the tier model and lifecycle

Once you have a working object, read these to understand how objects behave over time.

1. **[`docs/TIER_MODEL.md`](TIER_MODEL.md)** — Tier 1 (resident, default) vs Tier 3 (explicit deactivation). Covers
   when history compaction triggers, why Tier 2 (cold passivation) is absent in v1, and the
   `ListDurableObjectsAsync` visibility caveat.
2. **[`samples/03-scheduling`](../samples/03-scheduling/)** — Two patterns: `CreateDurableObjectScheduleAsync` (fresh execution
   per tick) and `CreateDurableObjectReminderAsync` (recurring delivery to a canonical object).
   Shows the required `Deactivate()` call for scheduled objects and reminder idempotency with
   `IReminderReceiver`.
3. **[`samples/04-object-to-object`](../samples/04-object-to-object/)** — The v1 workaround for DurableObject-to-DurableObject calls:
   an Activity that calls the target via `IDurableObjectFactory`. Shows why direct object-to-object
   messaging is deferred and the correct Activity-mediated pattern.

### Production readiness

Before deploying a DurableObject with live state you care about, read these in full.

1. **[`docs/FAILURE_HANDLING.md`](FAILURE_HANDLING.md) (full read)** — All sections: lifecycle hook failure taxonomy,
   authorization predicate + framework updates, reminder at-least-once delivery and idempotency,
   and the deactivation drain protocol.
2. **[`adr/004-versioning-strategy.md`](../adr/004-versioning-strategy.md)** — How to evolve objects safely. What changes are safe
   with no action (adding a query, internal logic changes), what requires `Workflow.Patched` (adding
   or renaming an update handler), and what is forbidden without a migration plan. Use
   `WorkflowReplayer` before each release.
3. **[`adr/005-signals-banned.md`](../adr/005-signals-banned.md)** — Why `[WorkflowSignal]` does not exist in this library.
   Three independent reasons: signals bypass the authorization hook, give callers no confirmation,
   and offer no rollback on partial state mutation. `DeactivateAsync` is `[WorkflowUpdate]` by
   design.

### Understanding design decisions

The ADRs document why the library is built the way it is. Read them in order for the full
architectural picture; read individual ones when a constraint surprises you.

- **[`adr/001-net10-only.md`](../adr/001-net10-only.md)** — Why the library ships `net10.0;net8.0;netstandard2.1`, what polyfills made it feasible, and the one method unavailable on .NET Standard.
- **[`adr/002-dispatchproxy-not-sourcegen.md`](../adr/002-dispatchproxy-not-sourcegen.md)** — Historical rationale for the remaining
  `DispatchProxy` compatibility fallback.
- **[`adr/009-generated-asynchronous-clients.md`](../adr/009-generated-asynchronous-clients.md)** — How generated clients add asynchronous
  queries and a NativeAOT-compatible client dispatch path.
- **[`adr/003-do-to-do-messaging-deferred.md`](../adr/003-do-to-do-messaging-deferred.md)** — Why object-to-object messaging is not in v1
  and the three candidate designs under evaluation for v1.1 (Activity-mediated, child workflows,
  Nexus).
- **[`adr/004-versioning-strategy.md`](../adr/004-versioning-strategy.md)** — Versioning strategy for long-lived objects.
- **[`adr/005-signals-banned.md`](../adr/005-signals-banned.md)** — Why signals are banned.

---

## Document Map

One-line description of every doc in this repository.

| File | Description |
|------|-------------|
| [`README.md`](../README.md) | Repository-level package overview, analyzer entry point, minimal Durable Objects example, documentation map, and contributor commands. |
| [`docs/ANALYZERS.md`](ANALYZERS.md) | General Temporal and Durable Objects analyzer rules, IDE code fixes, generated clients, and installation. |
| [`docs/DURABLE_OBJECTS.md`](DURABLE_OBJECTS.md) | Durable Objects concepts and API overview: selection guidance, identity, lifecycle, state, failures, scheduling, and NativeAOT. |
| [`docs/BOILERPLATE.md`](BOILERPLATE.md) | The four required patterns every DurableObject class must follow, with explanations of why each exists and the common mistakes. |
| [`docs/FAILURE_HANDLING.md`](FAILURE_HANDLING.md) | Complete failure taxonomy: client-side exception mapping, update handler and lifecycle hook exception behavior, authorization predicates, reminder idempotency, and deactivation drain protocol. |
| [`docs/TIER_MODEL.md`](TIER_MODEL.md) | Lifecycle tiers (Resident, Explicit Deactivation, Cold Passivation), when each is used, and why Tier 2 is absent in v1. |
| [`adr/001-net10-only.md`](../adr/001-net10-only.md) | Why the library ships `net10.0;net8.0;netstandard2.1`, what polyfills make it feasible, and the one method unavailable on .NET Standard. |
| [`adr/002-dispatchproxy-not-sourcegen.md`](../adr/002-dispatchproxy-not-sourcegen.md) | Historical rationale for the `DispatchProxy` fallback. |
| [`adr/009-generated-asynchronous-clients.md`](../adr/009-generated-asynchronous-clients.md) | Generated concrete clients, async query API, registry integration, and NativeAOT scope. |
| [`MAINTAINER_VERIFICATION.md`](MAINTAINER_VERIFICATION.md) | Replay, NativeAOT, benchmark, and bounded-scale verification. |
| [`adr/003-do-to-do-messaging-deferred.md`](../adr/003-do-to-do-messaging-deferred.md) | Why DurableObject-to-DurableObject messaging is not in v1 and the v1 Activity-mediated workaround. |
| [`adr/004-versioning-strategy.md`](../adr/004-versioning-strategy.md) | Safe vs. breaking changes for long-lived objects, `Workflow.Patched`, worker deployment strategy, and replay tests. |
| [`adr/005-signals-banned.md`](../adr/005-signals-banned.md) | Why `[WorkflowSignal]` is banned and the three conditions required to reconsider it in v1.1. |
| [`docs/TROUBLESHOOTING.md`](TROUBLESHOOTING.md) | Common mistakes and how to fix them: silent startup failures, non-determinism errors, object-not-found errors, scheduled objects that never deactivate, and more. |
| [`samples/README.md`](../samples/README.md) | Index of all six samples, suggested reading order, and common prerequisites. |
| [`samples/01-getting-started/`](../samples/01-getting-started/) | Core programming model: generated client, async query, factory, lifecycle hooks, and activity calls. |
| [`samples/02-input-validation/`](../samples/02-input-validation/) | `[WorkflowUpdateValidator]`, `DurableObjectNotFoundException`, `DurableObjectNotActiveException`, and catching the correct exception type. |
| [`samples/03-scheduling/`](../samples/03-scheduling/) | `CreateDurableObjectScheduleAsync` and `CreateDurableObjectReminderAsync`, reminder idempotency with `IReminderReceiver`. |
| [`samples/04-object-to-object/`](../samples/04-object-to-object/) | Activity-mediated DO-to-DO calls using `IDurableObjectFactory` inside activities. |
| [`samples/05-observability/`](../samples/05-observability/) | OpenTelemetry tracing, `TracingInterceptor`, and how interceptors compose. |
| [`samples/06-testing/`](../samples/06-testing/) | `WorkflowEnvironment.StartLocalAsync()`, xUnit integration test patterns, and test isolation. |

---

## Quick Links

- Samples index: [`samples/README.md`](../samples/README.md)
- Run a sample: `just run-sample` (sample 01) or `just run-sample <sample-name>`
- Durable Objects API overview: [`docs/DURABLE_OBJECTS.md#api-overview`](DURABLE_OBJECTS.md#api-overview)
- Required patterns explained: [`docs/BOILERPLATE.md`](BOILERPLATE.md)
- Troubleshooting: [`docs/TROUBLESHOOTING.md`](TROUBLESHOOTING.md)
- Report a bug: [GitHub Issues](https://github.com/temporal-community/temporal-dotnet-extensions/issues)
- Temporal .NET SDK: [github.com/temporalio/sdk-dotnet](https://github.com/temporalio/sdk-dotnet)
