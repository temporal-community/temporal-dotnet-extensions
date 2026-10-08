# Samples

The samples cover the Durable Objects runtime and the general Temporal workflow analyzers. Choose
the category that matches what you want to try; the analyzer examples do not require Durable Objects.

## Choose a sample

| Sample | Value demonstrated | Observable result | Command from repository root |
|---|---|---|---|
| [01: Getting started](01-getting-started/) | Address one entity with a generated client; perform activity work and query asynchronously | Three increments per run; rerunning against the same server adds to the existing counter if no other caller updates it | `just run-sample` |
| [02: Input validation](02-input-validation/) | Reject invalid mutations while retaining an authoritative closed-account state | Balance stays 300 after rejected withdrawal and post-closure deposit; missing query is distinguished | `just run-sample 02-input-validation` |
| [03: Scheduling](03-scheduling/) | Choose fresh periodic jobs or recurring updates to a stable entity | Distinct report executions and multiple reminder deliveries to one tracker | `just run-sample 03-scheduling` |
| [04: Object-to-object](04-object-to-object/) | Cross entity boundaries through an activity and deduplicate a repeated operation | Two fulfilled orders; repeating a confirmed order leaves reserved quantities at 5 and 3 | `just run-sample 04-object-to-object` |
| [05: Observability](05-observability/) | Trace client RPCs and worker execution through Temporal interceptors | Console spans for updates and activities; sensor gains three readings | `just run-sample 05-observability` |
| [06: Testing](06-testing/) | Test real workflow behavior with isolated task queues | xUnit assertions for mutation, queries, rejection, and missing objects | `dotnet test samples/06-testing` |
| [07: Analyzer file app](07-analyzer-file-app/) | Apply analyzer guardrails to a .NET file-based app | Expected TEMP001–TEMP004, TEMP007, TEMP008 diagnostics | `dotnet run --file samples/07-analyzer-file-app/Program.cs` |
| [08: Analyzer project](08-analyzer-project/) | Inspect diagnostics and code fixes in a conventional IDE project | Expected analyzer errors; build stays red until violations are fixed | `dotnet build samples/08-analyzer-project` |

For the Durable Objects path, start with 01 and 02, then select the topic that fits your application.
Samples 01–06 reference the runtime in this checkout. Samples 07–08 are general Temporal analyzer
examples pinned to analyzer package version `0.3.2`, so their diagnostics need not include rules
added only in the current source.

## Run Durable Objects demos (01–05)

Install the .NET 10 SDK and Temporal CLI, then start an external dev server:

```sh
temporal server start-dev
just run-sample
```

These samples default to `localhost:7233`; the server UI is normally at `http://localhost:8233`.
Override `Temporal__Address` when using a different endpoint. Each demo stops its host when finished
and returns a nonzero exit code if its result checks fail. The scheduling demo takes about
32 seconds to allow real schedule ticks.

Samples 01 and 05 deliberately leave their entities open so reruns can show accumulated state.
Sample 02 uses a fresh account ID per run and retains its domain-closed state. Samples 03 and 04
remove their schedules or deactivate their canonical objects after a successful demo.

Stateful samples use `DurableObjectBase<TState>` to carry state through Continue-as-New. This is
separate from domain closure or explicit deactivation: starting again after deactivation creates
cold state. Sample activities simulate storage and notifications with logs; they do not write to
an external database or send email. Production effects need idempotency at the destination, and
long-lived deduplication sets need an application-specific retention policy.

## Run SDK-managed integration tests (06)

Sample 06 needs no separately installed Temporal server or Docker: the SDK launches a local server
process for the tests. Its integration tests use real time, not time skipping. See the
[sample 06 guide](06-testing/README.md) and run:

```sh
dotnet test samples/06-testing
```

## Inspect general analyzer examples (07–08)

Samples 07 and 08 need no running Temporal server and do not use the Durable Objects runtime. They
intentionally fail compilation so you can inspect general Temporal analyzer diagnostics; sample 08
is a conventional IDE project, and sample 07 is a .NET 10 file-based app. See the
[file-based sample guide](07-analyzer-file-app/README.md) or
[project sample guide](08-analyzer-project/README.md) for the expected diagnostics and IDE fixes.
