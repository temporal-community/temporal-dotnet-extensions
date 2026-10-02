# Getting Started with Durable Objects

Use Durable Objects when a stable ID represents a long-lived entity and you want conventions for
serialized updates, state carry-forward, and lifecycle behavior. Familiarity with the Temporal
.NET SDK is assumed; start with its [documentation](https://github.com/temporalio/sdk-dotnet) if
Temporal is new to you.

## Start here

1. Read the [minimal example](../README.md#minimal-example) for the contract, typed state, worker
   registration, and generated client.
2. Run [sample 01](../samples/01-getting-started/) against a local Temporal server.
3. Read [concepts](DURABLE_OBJECTS.md) and its
   [implementation requirements](DURABLE_OBJECTS.md#implementation-requirements) before writing an object.
4. Run [sample 02](../samples/02-input-validation/) to see validator rejection, missing-object
   queries, and domain closure. Read [failure handling](FAILURE_HANDLING.md) for the exception model.

## Pick a topic

| Topic | Guide or sample |
|---|---|
| Resident execution, deactivation, and history compaction | [Lifecycle tiers](TIER_MODEL.md) |
| Fresh scheduled jobs versus recurring updates to one entity | [Sample 03](../samples/03-scheduling/) |
| Cross-object work and retry deduplication | [Sample 04](../samples/04-object-to-object/) |
| Client and worker tracing | [Sample 05](../samples/05-observability/) |
| Real-server integration tests and isolation | [Sample 06](../samples/06-testing/) |
| Analyzer diagnostics and IDE code fixes | [Analyzers](ANALYZERS.md), [sample 07](../samples/07-analyzer-file-app/), [sample 08](../samples/08-analyzer-project/) |
| Generated project and item templates | [Templates](TEMPLATES.md) |
| Configuration and runtime problems | [Troubleshooting](TROUBLESHOOTING.md) |

## Before deployment

- Understand [update failures, authorization, draining, and reminder delivery](FAILURE_HANDLING.md).
- Keep state snapshots compatible with existing executions and replay representative histories
  before changing workflow code. See [maintainer verification](MAINTAINER_VERIFICATION.md).
- Measure a representative workload with the [load runner and benchmarks](../benchmarks/README.md).
  Local dev-server results are not production capacity limits.

The [samples index](../samples/README.md) lists prerequisites, commands, and the value of each sample.
