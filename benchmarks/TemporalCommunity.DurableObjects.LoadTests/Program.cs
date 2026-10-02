using System.Globalization;
using System.Text.Json;
using Temporalio.Client;
using Temporalio.Testing;
using TemporalCommunity.DurableObjects.LoadTests;

if (args.Contains("--help", StringComparer.Ordinal))
{
    Console.WriteLine(LoadOptions.Help);
    return 0;
}

WorkflowEnvironment? environment = null;
try
{
    var options = LoadOptions.Parse(args);
    var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
    var taskQueue = $"durable-load-{runId}";
    var client = options.Address is null
        ? (environment = await WorkflowEnvironment.StartLocalAsync()).Client
        : await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(options.Address) { Namespace = options.Namespace });
    Console.WriteLine($"Load run {runId}: {options.Operation}, objects={options.Objects}, callers={options.Concurrency}, serialize={options.Serialize}");
    var report = await new LoadRunner(client, options, runId, taskQueue).RunAsync();
    var output = Path.GetFullPath(options.Output ?? Path.Combine("artifacts", "load-tests", runId + ".json"));
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    var summary = report.Summary;
    Console.WriteLine(FormattableString.Invariant($"Completed {summary.Completed}/{summary.Started}; failures={summary.Failed}; capacity/scheduler misses={summary.CapacityMisses}/{summary.SchedulerMisses}; max outstanding={summary.MaxOutstandingCalls}"));
    Console.WriteLine(FormattableString.Invariant($"Throughput offering={summary.CompletedPerSecondDuringOffering:F2}/s, including drain={summary.CompletedPerSecondIncludingDrain:F2}/s; p50/p95/p99={summary.SuccessfulCallLatency.P50Ms:F2}/{summary.SuccessfulCallLatency.P95Ms:F2}/{summary.SuccessfulCallLatency.P99Ms:F2}ms"));
    Console.WriteLine(FormattableString.Invariant($"Rollovers during offering/final={summary.RolloversObservedDuringOffering}/{summary.FinalRollovers}; max sampled history={summary.MaxSampledHistoryLength} events/{summary.MaxSampledHistoryBytes} bytes"));
    Console.WriteLine($"Report: {output}");
    foreach (var failure in report.Failures) await Console.Error.WriteLineAsync(failure);
    if (report.MonitoringErrors.Count != 0) await Console.Error.WriteLineAsync($"Monitoring errors: {report.MonitoringErrors.Count}; see report.");
    return report.Failures.Count == 0 ? 0 : 1;
}
catch (Exception error)
{
    await Console.Error.WriteLineAsync($"Load runner failed: {error.GetType().Name}: {error.Message}");
    return 1;
}
finally
{
    if (environment is not null) await environment.DisposeAsync();
}
