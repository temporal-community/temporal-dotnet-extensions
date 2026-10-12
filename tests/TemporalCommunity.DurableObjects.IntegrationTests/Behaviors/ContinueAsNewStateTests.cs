using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;
using static TemporalCommunity.DurableObjects.IntegrationTests.Behaviors.Slice3AcceptanceTests;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

/// <summary>
/// Drive enough updates to cross RollingCounter's low history threshold.
/// Verifies: run ID changes (CAN actually fired) AND count equals total updates (state survived).
/// </summary>
public sealed class ContinueAsNewStateTests : DurableObjectTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public ContinueAsNewStateTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ContinueAsNew_LogsFinalDecisionAndChangesRunId()
    {
        var id = $"continue-as-new-log-{Guid.NewGuid():N}";
        var taskQueue = UniqueTaskQueue();
        using var logs = new ContinueAsNewLogs();
        using var loggerFactory = logs.Factory;
        using var worker = TestWorkerBuilder.Build(
            Client, taskQueue, workflowTypes: [typeof(RollingCounter)], loggerFactory: loggerFactory);

        using var cts = new CancellationTokenSource();
        var running = worker.ExecuteAsync(cts.Token);
        var handle = Client.GetWorkflowHandle(id);
        try
        {
            var counter = TestFactory.Create(Client, taskQueue).Get<IRollingCounter>(id);
            await counter.IncrementAsync(1).WaitAsync(Timeout);
            var initialRunId = (await handle.DescribeAsync()).RunId;

            // Drive the low test threshold through the existing production rollover path.
            for (var i = 1; i < 50; i++)
            {
                await counter.IncrementAsync(1).WaitAsync(Timeout);
            }

            var eventSeen = false;
            var currentRunId = initialRunId;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < Timeout && (!eventSeen || currentRunId == initialRunId))
            {
                eventSeen = logs.Entries.Any(entry =>
                    entry.EventId.Id == 4102 &&
                    entry.EventId.Name == "DurableObjectContinueAsNew" &&
                    entry.Level == LogLevel.Information);
                currentRunId = (await handle.DescribeAsync()).RunId;
                if (!eventSeen || currentRunId == initialRunId)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                }
            }

            Assert.NotEqual(initialRunId, currentRunId);
            var rolloverLog = logs.Entries.FirstOrDefault(entry =>
                entry.EventId.Id == 4102 &&
                entry.EventId.Name == "DurableObjectContinueAsNew" &&
                entry.Level == LogLevel.Information);
            Assert.NotNull(rolloverLog);
            Assert.Equal(id, rolloverLog.Fields["WorkflowId"]);
            Assert.True(rolloverLog.Fields.TryGetValue("HistoryLength", out var historyLength));
            var numericHistoryLength = Assert.IsAssignableFrom<IConvertible>(historyLength);
            Assert.True(numericHistoryLength.ToInt64(CultureInfo.InvariantCulture) > 0);
        }
        finally
        {
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(cts, running);
        }
    }

    [Fact]
    public async Task ConcurrentUpdatesNearLowThreshold_AllSurviveContinueAsNew()
    {
        const int updates = 50;
        const string id = "scenario-b-concurrent-rollover";
        var tq = UniqueTaskQueue();
        using var worker = TestWorkerBuilder.Build(
            Client, tq, workflowTypes: [typeof(RollingCounter)]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            var factory = TestFactory.Create(Client, tq);
            var counter = await factory.GetOrCreateAsync<IRollingCounter>(id);
            var initialRunId = (await Client.GetWorkflowHandle(id).DescribeAsync()).RunId;
            var tasks = Enumerable.Range(0, updates).Select(_ => counter.IncrementAsync(1)).ToArray();
            await Task.WhenAll(tasks);

            var finalRunId = (await Client.GetWorkflowHandle(id).DescribeAsync()).RunId;
            Assert.Equal(updates, counter.GetCount());
            Assert.NotEqual(initialRunId, finalRunId);
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task State_SurvivesContinueAsNew_AndRunIdChanges()
    {
        const int updates = 25;
        var tq = UniqueTaskQueue();
        const string id = "scenario-b-obj";

        using var worker = TestWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(RollingCounter)]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            var factory = TestFactory.Create(Client, tq);
            var counter = factory.Get<IRollingCounter>(id);

            // First update cold-starts the object. Capture initial run ID.
            Assert.Equal(1, await counter.IncrementAsync(1));
            var initialRunId = (await Client.GetWorkflowHandle(id).DescribeAsync()).RunId;
            var observedRunIds = new HashSet<string>(StringComparer.Ordinal) { initialRunId };

            // Drive remaining updates to force CAN (threshold is 30 history events).
            for (var i = 1; i < updates; i++)
            {
                await counter.IncrementAsync(1);
                observedRunIds.Add((await Client.GetWorkflowHandle(id).DescribeAsync()).RunId);
            }

            var finalRunId = (await Client.GetWorkflowHandle(id).DescribeAsync()).RunId;
            var count = counter.GetCount();

            // State survived CAN.
            Assert.Equal(updates, count);
            // Run ID changed — CAN actually fired.
            Assert.NotEqual(initialRunId, finalRunId);
            // The low threshold produces at least two Continue-as-New boundaries.
            Assert.True(observedRunIds.Count >= 3, $"Expected at least 3 runs, observed {observedRunIds.Count}.");

            await counter.DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    private sealed record CapturedLog(LogLevel Level, EventId EventId,
        IReadOnlyDictionary<string, object?> Fields);

    private sealed class ContinueAsNewLogs : ILoggerProvider
    {
        public ConcurrentQueue<CapturedLog> Entries { get; } = new();
        public ILoggerFactory Factory { get; }

        public ContinueAsNewLogs() =>
            Factory = LoggerFactory.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Information).AddProvider(this));

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { } // The owning LoggerFactory is disposed separately.

        private sealed class CapturingLogger(ContinueAsNewLogs owner) : ILogger
        {
            public bool IsEnabled(LogLevel logLevel) => true;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                    : new Dictionary<string, object?>();
                owner.Entries.Enqueue(new CapturedLog(logLevel, eventId, fields));
            }
        }
    }
}
