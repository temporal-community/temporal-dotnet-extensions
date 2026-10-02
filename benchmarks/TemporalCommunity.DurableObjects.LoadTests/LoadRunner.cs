using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using Temporalio.Worker;

namespace TemporalCommunity.DurableObjects.LoadTests;

public sealed class LoadRunner(ITemporalClient temporal, LoadOptions options, string runId, string taskQueue)
{
    private readonly ConcurrentQueue<CallSample> _calls = new();
    private readonly ConcurrentQueue<ObservationSample> _observations = new();
    private readonly ConcurrentQueue<string> _monitoringErrors = new();
    private readonly List<string> _failures = [];
    private readonly Stopwatch _clock = new();
    private int _outstanding;
    private int _maxOutstanding;
    private long _sequence;
    private long _offered;
    private long _capacityMisses;
    private long _schedulerMisses;
    private long _lastOfferedTicks;
    private readonly string _payload = new('x', options.PayloadBytes);

    public async Task<LoadReport> RunAsync()
    {
        var ids = Enumerable.Range(0, options.Objects).Select(i => $"load/{runId}/{i}").ToArray();
        using var services = new ServiceCollection()
            .AddSingleton(temporal)
            .AddDurableObjects(taskQueue)
            .BuildServiceProvider();
        var factory = services.GetRequiredService<IDurableObjectFactory>();
        var callOptions = new DurableObjectCallOptions(rpcTimeout: TimeSpan.FromSeconds(options.RpcTimeoutSeconds));
        var clients = ids.Select(id => (LoadCounterDurableObjectClient)factory.Get<ILoadCounter>(id, callOptions)).ToArray();
        var workerOptions = new TemporalWorkerOptions(taskQueue);
        workerOptions.AddDurableObjectWorkflows(typeof(LoadCounter).Assembly,
            new DurableObjectWorkerOptions { Serialize = options.Serialize });
        workerOptions.AddAllActivities(new LoadActivities());
        using var worker = new TemporalWorker(temporal, workerOptions);
        using var workerStop = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(workerStop.Token);
        try
        {
            // Configure and verify readiness outside the measurement window.
            await Task.WhenAll(clients.Select(client => client.ConfigureAsync(options.HistoryThreshold)));
            await Task.WhenAll(clients.Select(client => client.ReadObservationAsync()));
            using var monitorStop = new CancellationTokenSource();
            _clock.Start();
            var monitor = MonitorAsync(clients, monitorStop.Token);
            try
            {
                if (options.ArrivalRate == 0)
                {
                    await Task.WhenAll(Enumerable.Range(0, options.Concurrency).Select(_ => CallerLoopAsync(clients)));
                }
                else
                {
                    await FixedRateAsync(clients);
                }
            }
            finally
            {
                await monitorStop.CancelAsync();
                await monitor;
            }

            var final = await SettleAsync(clients);
            var elapsed = _clock.Elapsed.TotalSeconds;
            return CreateReport(final, elapsed);
        }
        finally
        {
            // IDs and task queue are unique to this invocation, including on an external server.
            foreach (var id in ids)
            {
                try
                {
                    await temporal.GetWorkflowHandle(id).TerminateAsync("load run cleanup",
                        new WorkflowTerminateOptions { Rpc = new RpcOptions { Timeout = TimeSpan.FromSeconds(5) } });
                }
                catch (Exception error)
                {
                    var message = $"Cleanup of {id}: {error.GetType().Name}: {error.Message}";
                    _failures.Add(message);
                    await Console.Error.WriteLineAsync(message);
                }
            }

            await workerStop.CancelAsync();
            try { await workerTask; } catch (OperationCanceledException) { }
        }
    }

    private async Task CallerLoopAsync(LoadCounterDurableObjectClient[] clients)
    {
        while (_clock.Elapsed.TotalSeconds < options.DurationSeconds)
        {
            var sequence = Interlocked.Increment(ref _sequence);
            if (sequence > options.MaxOperations) break;
            Interlocked.Increment(ref _offered);
            await CallAsync(sequence, clients);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025",
        Justification = "Every task added to pending is awaited in finally before the semaphore is disposed.")]
    private async Task FixedRateAsync(LoadCounterDurableObjectClient[] clients)
    {
        using var capacity = new SemaphoreSlim(options.Concurrency);
        var pending = new List<Task>();
        try
        {
            var targetArrivals = Math.Min(options.MaxOperations, (long)options.DurationSeconds * options.ArrivalRate);
            for (long sequence = 1; sequence <= targetArrivals; sequence++)
            {
                var scheduledSeconds = (sequence - 1) / (double)options.ArrivalRate;
                if (scheduledSeconds >= options.DurationSeconds) break;
                var delay = scheduledSeconds - _clock.Elapsed.TotalSeconds;
                if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay));
                if (_clock.Elapsed.TotalSeconds >= options.DurationSeconds)
                {
                    var remaining = targetArrivals - sequence + 1;
                    Interlocked.Add(ref _offered, remaining);
                    Interlocked.Add(ref _schedulerMisses, remaining);
                    break;
                }
                Interlocked.Increment(ref _offered);
                Interlocked.Exchange(ref _lastOfferedTicks, _clock.ElapsedTicks);
                // Do not burst old arrivals after a scheduling pause. Report them as missed by
                // the generator; capacity misses separately report its outstanding-call limit.
                if (_clock.Elapsed.TotalSeconds - scheduledSeconds >= 1d / options.ArrivalRate)
                {
                    Interlocked.Increment(ref _schedulerMisses);
                }
                else if (!capacity.Wait(0))
                {
                    Interlocked.Increment(ref _capacityMisses);
                }
                else
                {
                    pending.Add(CallWithCapacityAsync(sequence, clients, capacity));
                }
            }
        }
        finally
        {
            await Task.WhenAll(pending);
        }
    }

    private async Task CallWithCapacityAsync(long sequence, LoadCounterDurableObjectClient[] clients, SemaphoreSlim capacity)
    {
        try { await CallAsync(sequence, clients); }
        finally { capacity.Release(); }
    }

    private async Task CallAsync(long sequence, LoadCounterDurableObjectClient[] clients)
    {
        var index = (int)((sequence - 1) % clients.Length);
        var client = clients[index];
        var started = _clock.Elapsed.TotalSeconds;
        Interlocked.Exchange(ref _lastOfferedTicks, _clock.ElapsedTicks);
        var outstanding = Interlocked.Increment(ref _outstanding);
        int previous;
        do { previous = Volatile.Read(ref _maxOutstanding); }
        while (outstanding > previous && Interlocked.CompareExchange(ref _maxOutstanding, outstanding, previous) != previous);
        string? error = null;
        try
        {
            switch (options.Operation)
            {
                case "update":
                    await client.AddAsync(_payload, options.ActivityDelayMs);
                    break;
                case "query-async":
                    await client.ReadObservationAsync();
                    break;
                case "query-sync":
                    // Simulates concurrent callers that park thread-pool threads during an RPC.
                    await Task.Run(() => ((ILoadCounter)client).ReadObservation());
                    break;
            }
        }
        catch (Exception exception)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
        }
        finally
        {
            var completed = _clock.Elapsed.TotalSeconds;
            _calls.Enqueue(new(sequence, index, started, completed, (completed - started) * 1000, error));
            Interlocked.Decrement(ref _outstanding);
        }
    }

    private async Task MonitorAsync(LoadCounterDurableObjectClient[] clients, CancellationToken cancellationToken)
    {
        using var process = Process.GetCurrentProcess();
        var rpc = new DurableObjectCallOptions(rpcTimeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                for (var i = 0; i < clients.Length; i++)
                {
                    try
                    {
                        var observed = await clients[i].ReadObservationAsync(rpc);
                        process.Refresh();
                        _observations.Enqueue(new(_clock.Elapsed.TotalSeconds, i, observed,
                            Volatile.Read(ref _outstanding), ThreadPool.ThreadCount,
                            ThreadPool.PendingWorkItemCount, process.WorkingSet64));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                    catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                    {
                        _monitoringErrors.Enqueue($"Object {i}: {error.GetType().Name}: {error.Message}");
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task<CounterObservation[]> SettleAsync(LoadCounterDurableObjectClient[] clients)
    {
        var deadline = _clock.Elapsed + TimeSpan.FromSeconds(options.DrainSeconds);
        CounterObservation[] final;
        do
        {
            final = await Task.WhenAll(clients.Select(client => client.ReadObservationAsync()));
            if (final.All(item => item.InFlightHandlers == 0)) return final;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        while (_clock.Elapsed < deadline);
        return final;
    }

    private LoadReport CreateReport(CounterObservation[] final, double elapsed)
    {
        var calls = _calls.OrderBy(call => call.Sequence).ToArray();
        var observed = _observations.ToArray();
        var successful = calls.Where(call => call.Error is null).ToArray();
        var failed = calls.Where(call => call.Error is not null).ToArray();
        var limited = _offered >= options.MaxOperations;
        var offeringSeconds = limited
            ? Math.Clamp((double)_lastOfferedTicks / Stopwatch.Frequency, .000001, options.DurationSeconds)
            : options.DurationSeconds;
        var rate = successful.Count(call => call.CompletedSeconds <= offeringSeconds) / offeringSeconds;
        var latency = LatencySummary.From(successful.Select(call => call.LatencyMs));
        var liveRollovers = observed.Where(sample => sample.ElapsedSeconds <= offeringSeconds)
            .GroupBy(sample => sample.ObjectIndex)
            .Sum(group => Math.Max(0, group.Max(sample => sample.Observation.Activations) - 1));
        var aboveThresholdSeconds = observed
            .Where(sample => sample.Observation.HistoryLength >= sample.Observation.HistoryThreshold)
            .GroupBy(sample => (sample.ObjectIndex, sample.Observation.RunId))
            .Select(group => group.Max(sample => sample.ElapsedSeconds) - group.Min(sample => sample.ElapsedSeconds))
            .DefaultIfEmpty().Max();
        var failures = _failures;
        if (failed.Length != 0) failures.Add($"{failed.Length} client calls failed; accepted-but-unconfirmed updates are possible. No application retries were issued.");
        for (var i = 0; i < final.Length; i++)
        {
            var expected = options.Operation == "update" ? successful.Count(call => call.ObjectIndex == i) : 0;
            if (final[i].CompletedUpdates != expected)
                failures.Add($"Object {i}: state has {final[i].CompletedUpdates} completed updates; {expected} successful updates were observed by the caller.");
            if (final[i].InFlightHandlers != 0) failures.Add($"Object {i}: handlers did not drain before settlement timeout.");
        }

        if (successful.Length == 0) failures.Add("No successful calls were measured.");
        if (options.RequireRollover && liveRollovers == 0) failures.Add("No rollover was observed during the offering window.");
        if (options.P95LimitMs is { } limit && latency.P95Ms > limit) failures.Add($"p95 {latency.P95Ms:F2}ms exceeds {limit:G}ms.");
        if (options.MinThroughput is { } minimum && rate < minimum) failures.Add($"Offering-window throughput {rate:F2}/s is below {minimum:F2}/s.");
        var summary = new LoadSummary(_offered, calls.Length, successful.Length, failed.Length, _capacityMisses, _schedulerMisses,
            _maxOutstanding, limited, offeringSeconds, elapsed, rate, successful.Length / elapsed,
            latency, LatencySummary.From(failed.Select(call => call.LatencyMs)), liveRollovers,
            final.Sum(item => item.Activations - 1), observed.Select(sample => sample.Observation.HistoryLength).DefaultIfEmpty().Max(),
            observed.Select(sample => sample.Observation.HistoryBytes).DefaultIfEmpty().Max(), aboveThresholdSeconds,
            (long)options.PayloadBytes * final.Count(item => item.CompletedUpdates > 0));
        return new(runId, DateTimeOffset.UtcNow, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
            typeof(DurableObjectBase).Assembly.GetName().Version?.ToString() ?? "unknown",
            options.Address is null ? "embedded-real-time-dev-server" : "external", taskQueue,
            options, summary, failures, _monitoringErrors.ToArray(), final, calls, observed);
    }
}
