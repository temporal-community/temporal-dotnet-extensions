# Getting started with Temporal .NET extensions

Choose one of these independent paths based on what you want to try. The paths assume familiarity
with .NET and the Temporal .NET SDK.

## Start with templates

Choose this path when you want a runnable plain Temporal .NET starter rather than the Durable
Objects programming model.

- **Prerequisites:** .NET 10 SDK, the `TemporalCommunity.Templates` package installed with
  `dotnet new install TemporalCommunity.Templates`, and access to a Temporal server. For local
  development, the standalone solution uses the Temporal CLI to start a dev server; an Aspire
  solution can provision one instead.
- **First action:** Generate the standalone Worker + Client + Shared solution:

  ```bash
  dotnet new temporal-solution -n Contoso.Fulfillment -o Contoso.Fulfillment
  ```

  Follow [Run a generated solution](templates.md#run-a-generated-solution) to start its Worker and
  one-shot Client against the same server.
- **Expected result:** The Client starts the sample workflow, waits for its result, logs it, and
  exits; the Worker continues running. A generated worker-only project is a long-running worker
  and does not submit work. Workflow, activity, and converter item templates add code to a project;
  they are not executable applications on their own.
- **Next step:** Explore [template types and options](templates.md), including worker-only,
  item-template, Aspire, and OpenTelemetry alternatives.

## Start with general analyzers

Choose this path to see replay-safety diagnostics for ordinary Temporal workflows without adopting
Durable Objects.

- **Prerequisites:** .NET 10 SDK. No Temporal server or Durable Objects runtime is needed.
- **First action:** Build the conventional analyzer project sample:

  ```bash
  dotnet build samples/08-analyzer-project
  ```

- **Expected result:** The build intentionally fails with analyzer diagnostics for the violations
  in the sample (`TEMP001`–`TEMP004`, `TEMP007`, and `TEMP008`). Open it as a project in an IDE to
  inspect the available code fixes; do not run it expecting a workflow result. Sample 07 provides
  a .NET file-based alternative. Both examples consume analyzer package version `0.3.2`, so their
  diagnostics may not include rules added only in this source tree.
- **Next step:** Review the [general Temporal rule catalog and limitations](analyzers.md#general-temporal-rules),
  then choose the [project](../samples/08-analyzer-project/) or
  [file-based](../samples/07-analyzer-file-app/) example that fits your workflow.

## Start with Durable Objects

Choose this path when a stable ID represents a long-lived entity—such as a counter, account, or
device—and you want serialized updates and managed lifecycle behavior.

- **Prerequisites:** .NET 10 SDK, [just](https://github.com/casey/just) for the command below,
  Temporal CLI, and an external Temporal server at
  `localhost:7233` (or configure the sample for another endpoint).
- **First action:** Start a local server with `temporal server start-dev`, then run
  `just run-sample` from the repository root. This runs [sample 01](../samples/01-getting-started/).
- **Expected result:** The worker and demo caller update the `home` page counter three times through
  a generated client and query the count asynchronously. Each run adds three to the count retained
  by the same server, provided no other caller updates that object concurrently. The activity logs
  a simulated write; it does not persist to a real database.
- **Next step:** Read [Durable Objects concepts](durable-objects.md) and its
  [implementation requirements](durable-objects.md#implementation-requirements) before creating
  your own object. Then run [sample 02](../samples/02-input-validation/) to observe validator
  rejection, a query for a missing object, and domain closure. Its state also remains in Temporal;
  the sample has no activities and does not write to an external database.

## Continue with Durable Objects

The following topics are specific to the Durable Objects runtime:

| Topic | Guide or sample |
|---|---|
| Resident execution, deactivation, and history compaction | [Lifecycle tiers](tier-model.md) |
| Fresh scheduled jobs versus recurring updates to one entity | [Sample 03](../samples/03-scheduling/) |
| Cross-object work through an activity and repeated-operation deduplication | [Sample 04](../samples/04-object-to-object/) |
| Client and worker tracing | [Sample 05](../samples/05-observability/) |
| SDK-managed real-server integration tests | [Sample 06](../samples/06-testing/) |
| Durable Objects analyzers and generated clients | [Analyzer guide](analyzers.md#durableobjects-rules) |
| Runtime configuration and troubleshooting | [Troubleshooting](troubleshooting.md) |
| Failure handling, authorization, and reminder delivery | [Failure handling](failure-handling.md) |
| Workflow and snapshot compatibility before deployment | [Implementation requirements](durable-objects.md#implementation-requirements) |
| Load testing and benchmarks | [Load runner and benchmarks](../benchmarks/README.md) |

The [samples index](../samples/README.md) lists each sample's prerequisites, command, and observable
result. Samples 01–05 need an external Temporal server, sample 06 starts an SDK-managed local
server, and analyzer samples 07–08 need no server.
