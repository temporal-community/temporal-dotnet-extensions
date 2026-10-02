# Performance testing

The repository has two complementary tools. BenchmarkDotNet measures local client construction
and dispatch overhead. The load runner exercises actual Temporal RPCs and workflow executions for
a bounded offering window, then drains calls and checks durable state.

| Tool | Useful questions | Limits |
|---|---|---|
| BenchmarkDotNet | How much CPU time and allocation does generated client creation or query dispatch add compared with the compatibility proxy? | Its query interceptor immediately returns a value; it measures no network, server, payload conversion, replay, or contention. |
| Temporal load runner | How do one busy ID and many independent IDs behave? Does rollover progress during traffic? What are latency, outstanding calls, history growth, and caller thread-pool observations? | Results include the worker, SDK, transport, and server environment. The embedded dev server is useful for reproducible local diagnosis; production capacity needs a representative external server and hardware. |

## BenchmarkDotNet

Run on a quiet machine in Release mode:

```sh
dotnet run --project benchmarks/TemporalCommunity.DurableObjects.Benchmarks -c Release -- --filter '*'
```

`ClientCreationBenchmarks` compares generated clients with compatibility proxies.
`QueryDispatchBenchmarks` compares generated synchronous and asynchronous queries with synchronous
compatibility queries. It uses a real lazy SDK client with an outbound interceptor that returns an
already-completed task, avoiding network I/O and fake-call recording overhead. The synchronous and
asynchronous measurements describe local dispatch; they cannot establish thread-pool behavior
under RPC latency. `MemoryDiagnoser` reports allocations in the benchmark process.

For an execution smoke check:

```sh
dotnet run --project benchmarks/TemporalCommunity.DurableObjects.Benchmarks -c Release -- --filter '*QueryDispatchBenchmarks*' --job Dry
```

A Dry run verifies that each benchmark executes. Its single measured iteration is not a stable
performance baseline. Use the normal job for conclusions. See the official
[BenchmarkDotNet overview](https://benchmarkdotnet.org/articles/overview.html) and
[job settings](https://benchmarkdotnet.org/articles/configs/jobs.html).

## Automated load runs

Use .NET 10 and run from the repository root. The default starts an embedded Temporal dev server
with real time (no time skipping), creates a unique task queue and workflow IDs, and cleans up
those workflows after the run. An external plaintext test server with an existing namespace can
be selected with `--address localhost:7233 --namespace default`. The harness hosts its worker in
the same process as its callers; this should be held constant in comparative runs.

```sh
just load-test --help
# Equivalent entry point:
dotnet run --project benchmarks/TemporalCommunity.DurableObjects.LoadTests -c Release -- --help
```

Use `just load-test` with these profiles, or substitute the equivalent `dotnet run` entry point:

```sh
# One busy ID, serialized updates awaiting a real activity. Inspect rollover and history growth.
just load-test --duration-seconds 20 --objects 1 --concurrency 16 --activity-delay-ms 25 --history-threshold 100

# Independent IDs at the same total caller concurrency. Compare with the busy-ID profile.
just load-test --duration-seconds 20 --objects 16 --concurrency 16 --activity-delay-ms 25 --history-threshold 100

# Fixed offered rate with a bounded outstanding-call budget. Capacity misses are reported.
just load-test --duration-seconds 20 --objects 1 --concurrency 32 --arrival-rate 100 --activity-delay-ms 25 --history-threshold 100

# Safe overlapping handlers for comparison. This specific handler reads state after its await.
just load-test --duration-seconds 20 --objects 1 --concurrency 16 --serialize false --activity-delay-ms 25 --history-threshold 100

# Query callers: compare RPC latency and sampled caller thread-pool behavior.
just load-test --duration-seconds 20 --objects 16 --concurrency 64 --operation query-async
just load-test --duration-seconds 20 --objects 16 --concurrency 64 --operation query-sync

# Increase update payload and carried snapshot content; sweep 0, 16384, 131072 independently.
just load-test --duration-seconds 20 --objects 16 --concurrency 16 --activity-delay-ms 0 --payload-bytes 131072
```

Closed-loop callers await each response before sending another call. That represents bounded
concurrency, but it reduces the offered rate when the system slows. `--arrival-rate` schedules
arrivals independently of response completion while limiting outstanding calls to `--concurrency`.
An arrival skipped because the budget is full is a **capacity miss**. An arrival the generator
cannot schedule within one arrival interval is a **scheduler miss**; delayed arrivals are skipped
instead of being sent as a catch-up burst. Both counts must accompany successful-call latency.
Do not interpret the latter as the latency of all attempted arrivals. Monitoring queries add
load and can themselves be delayed; their errors are retained in the report.

Reports are JSON files under `artifacts/load-tests/`, or at `--output PATH`. They contain options,
environment metadata, raw call timings, per-object final state, and sampled observations:

- p50/p95/p99/max caller latency for successful and failed calls separately, including calls that
  finish during drain; throughput completed during the offering window and including drain.
- Started, completed, failed, offered, missed arrivals, and peak outstanding client calls.
  Outstanding client calls and in-body handlers are separate; neither is a measurement of the
  Temporal server's task-queue backlog.
- Run IDs, activation counts, history event/byte samples, and time sampled above the rollover
  threshold. History values exposed by the SDK can lag the current task; the measured maximum is
  a sampled lower bound. Rollover during traffic is reported separately from final rollover after
  traffic stops.
- Caller/worker process working set, thread-pool thread count, and pending thread-pool work items.
  These include the harness and monitoring overhead.
- Raw ASCII payload bytes retained across objects. This is the payload portion of carried state,
  not the complete serialized snapshot size. The runner does not measure replay duration,
  reminder delivery latency, or server task backlog.

Every run checks that each object's final count matches that object's successful update calls,
and that active handler bodies drain. Query profiles must leave mutation counts at zero. It makes
no application-level retries after a failed RPC. A failure can represent an accepted-but-unconfirmed
update, so the report keeps transport failures and any state discrepancy rather than calling it
an at-most-once guarantee. `--max-operations` bounds call/report storage; reaching it is reported
and shortens the offering window.

## Gates and interpreting results

The process exits nonzero on failed calls, mismatched durable state, undrained handlers, or a
configured gate. Throughput and latency gates are optional because portable machine-independent
limits have not been established:

```sh
just load-test --duration-seconds 20 --objects 16 --concurrency 16 --p95-limit-ms 1000 --min-throughput 10
just load-test --duration-seconds 20 --objects 1 --concurrency 16 --history-threshold 100 --require-rollover
```

The numbers in the first command are illustrative; choose limits from repeatable measurements in
your target environment. `--require-rollover` specifically requires a rollover **observed during
the offering window**. A rollover only after load stops fails that gate. Missed arrivals are
reported as overload/generator diagnostics and do not fail a run by themselves.

For CI, run a short bounded correctness profile and archive its JSON. Run sustained profiles on
a controlled performance runner or representative staging server, recording repeated runs and
varying one factor at a time. Increasing `--concurrency` is not proof of sustainable throughput:
look at failed calls, missed arrivals, tail latency, history growth, and rollover progress together.

The initial five-second local busy-ID run completed 127/127 updates with eight callers and a
10ms activity delay. No rollover was observed during load despite a 100-event threshold; sampled
history reached 1,348 events, and one rollover occurred after load stopped. This reproduced a drain
delay for that workload. It is a local observation, not a production capacity estimate or a proof
that rollover will be delayed for every workload.

Initial execution checks also covered independent IDs with carried payloads, fixed-rate arrivals,
overlapping handlers, both query modes, and deliberately failing gates. The independent-ID run
completed 368/368 updates and observed rollover during load; one monitoring query was rejected
with `DurableObjectNotActiveException` and status `ContinuedAsNew`. That error remains visible in
its report even though the update and final-state checks passed. The fixed-rate run accounted for
300 offered arrivals as 82 completed calls and 218 capacity misses. These are short harness
validation runs, not performance baselines.
