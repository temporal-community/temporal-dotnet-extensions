using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Scenarios;

/// <summary>
/// Scenario B: Drive enough updates to cross RollingCounter's low history threshold.
/// Verifies: run ID changes (CAN actually fired) AND count equals total updates (state survived).
/// </summary>
public sealed class ScenarioB_ContinueAsNewStateSurvival : DurableObjectTestBase
{
    public ScenarioB_ContinueAsNewStateSurvival(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task State_SurvivesContinueAsNew_AndRunIdChanges()
    {
        const int updates = 25;
        var tq = UniqueTaskQueue();
        const string id = "scenario-b-obj";

        using var worker = ScenarioWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(RollingCounter)]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            var factory = TestFactory.Create(Client, tq);
            var counter = factory.Get<IRollingCounter>(id);

            // First update cold-starts the object. Capture initial run ID.
            await counter.IncrementAsync(1);
            var initialRunId = (await Client.GetWorkflowHandle(id).DescribeAsync()).RunId;

            // Drive remaining updates to force CAN (threshold is 30 history events).
            for (var i = 1; i < updates; i++)
            {
                await counter.IncrementAsync(1);
            }

            var finalRunId = (await Client.GetWorkflowHandle(id).DescribeAsync()).RunId;
            var count = counter.GetCount();

            // State survived CAN.
            Assert.Equal(updates, count);
            // Run ID changed — CAN actually fired.
            Assert.NotEqual(initialRunId, finalRunId);

            await counter.DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
