# Samples

Samples 01–05 are console demos using a real Temporal server. Sample 06 starts its own local
server through the SDK. Samples 07–08 intentionally fail compilation to demonstrate analyzer
errors and IDE fixes.

## Choose a sample

| Sample | Value demonstrated | Observable result | Command from repository root |
|---|---|---|---|
| [01: Getting started](01-getting-started/) | Address one entity with a generated client; perform activity work and query asynchronously | Three increments per run; rerunning against the same server adds to the existing counter | `just run-sample` |
| [02: Input validation](02-input-validation/) | Reject invalid mutations while retaining an authoritative closed-account state | Balance stays 300 after rejected withdrawal and post-closure deposit; missing query is distinguished | `just run-sample 02-input-validation` |
| [03: Scheduling](03-scheduling/) | Choose fresh periodic jobs or recurring updates to a stable entity | Distinct report executions and multiple reminder deliveries to one tracker | `just run-sample 03-scheduling` |
| [04: Object-to-object](04-object-to-object/) | Cross entity boundaries through an activity and deduplicate a repeated operation | Two fulfilled orders; retrying an order leaves reserved quantities at 5 and 3 | `just run-sample 04-object-to-object` |
| [05: Observability](05-observability/) | Trace client RPCs and worker execution through Temporal interceptors | Console spans for updates and activities; sensor gains three readings | `just run-sample 05-observability` |
| [06: Testing](06-testing/) | Test real workflow behavior with isolated task queues | xUnit assertions for mutation, queries, rejection, and missing objects | `dotnet test samples/06-testing` |
| [07: Analyzer file app](07-analyzer-file-app/) | Apply analyzer guardrails to a .NET file-based app | Expected TEMP001–TEMP004, TEMP007, TEMP008 diagnostics | `dotnet run --file samples/07-analyzer-file-app/Program.cs` |
| [08: Analyzer project](08-analyzer-project/) | Inspect diagnostics and code fixes in a conventional IDE project | Expected analyzer errors; build stays red until violations are fixed | `dotnet build samples/08-analyzer-project` |

Start with 01 and 02, then select the topic that fits your application. The first six samples
reference the runtime in this checkout. Samples 07–08 are pinned consumers of the published
analyzer package, so their diagnostics need not include rules added only in the current source.

## Run the Durable Objects demos

Install the .NET 10 SDK and Temporal CLI, then start a dev server:

```sh
temporal server start-dev
just run-sample
```

The demos default to `localhost:7233`; the server UI is normally at `http://localhost:8233`.
Override `Temporal__Address` when using a different endpoint. Demos stop their host when finished
and return a nonzero exit code if their result checks fail. The scheduling demo takes about
32 seconds to allow real schedule ticks.

Samples 01 and 05 deliberately leave their entities open so reruns can show accumulated state.
Sample 02 uses a fresh account ID per run and retains its domain-closed state. Samples 03 and 04
remove their schedules or deactivate their canonical objects after a successful demo.

Stateful samples use `DurableObjectBase<TState>` to carry state through Continue-as-New. This is
separate from domain closure or explicit deactivation: starting again after deactivation creates
cold state. Sample activities simulate storage and notifications with logs; they do not write to
an external database or send email. Production effects need idempotency at the destination, and
long-lived deduplication sets need an application-specific retention policy.

Sample 06 needs no separately installed server or Docker: the SDK launches a local server process.
Its integration tests use real time, not time skipping. Samples 07 and 08 need no running server.
