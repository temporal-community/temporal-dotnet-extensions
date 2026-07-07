using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Scenarios;

/// <summary>
/// Scenario I: Two concurrent slow updates that use RunSerializedAsync must run turn-by-turn.
/// If they were reentrant, both would observe the other's increment after the await and return
/// {2, 2}. Serialized, they return {1, 2}. Final count must be 2.
/// </summary>
public sealed class ScenarioI_SerializedUpdates : DurableObjectTestBase
{
    public ScenarioI_SerializedUpdates(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task TwoConcurrentSlowUpdates_Return_One_Two_NotTwo_Two()
    {
        const string id = "scenario-i-obj";
        var tq = UniqueTaskQueue();

        using var worker = ScenarioWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(AuditedCounter)],
            activityInstances: [new ScenarioActivities()]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            var factory = TestFactory.Create(Client, tq);

            var t1 = factory.Get<IAuditedCounter>(id).IncrementSlowlySerializedAsync(1);
            var t2 = factory.Get<IAuditedCounter>(id).IncrementSlowlySerializedAsync(1);
            var results = await Task.WhenAll(t1, t2);
            var ordered = results.OrderBy(x => x).ToArray();

            var count = factory.Get<IAuditedCounter>(id).GetCount();

            // Serialized: one returned 1, other returned 2 (not both 2).
            Assert.Equal([1, 2], ordered);
            Assert.Equal(2, count);

            await factory.Get<IAuditedCounter>(id).DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
