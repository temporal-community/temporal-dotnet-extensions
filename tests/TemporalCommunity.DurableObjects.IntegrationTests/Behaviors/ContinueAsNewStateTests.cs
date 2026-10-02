using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

/// <summary>
/// Drive enough updates to cross RollingCounter's low history threshold.
/// Verifies: run ID changes (CAN actually fired) AND count equals total updates (state survived).
/// </summary>
public sealed class ContinueAsNewStateTests : DurableObjectTestBase
{
    public ContinueAsNewStateTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

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
}
