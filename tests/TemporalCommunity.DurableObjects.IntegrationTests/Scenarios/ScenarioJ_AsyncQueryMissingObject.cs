using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Scenarios;

/// <summary>
/// Scenario J: QueryDurableObjectAsync returns the correct value for an active object.
/// A query against a missing object throws DurableObjectNotFoundException (not a default value).
/// </summary>
public sealed class ScenarioJ_AsyncQueryMissingObject : DurableObjectTestBase
{
    public ScenarioJ_AsyncQueryMissingObject(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Query_ReturnsValue_AndMissingObjectThrowsNotFound()
    {
        const string id = "scenario-j-obj";
        const string missingId = "scenario-j-missing";
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

            // Query a missing object — must throw DurableObjectNotFoundException.
            var missingThrewNotFound = false;
            try
            {
                await factory.QueryDurableObjectAsync<int>(missingId, "GetCount");
            }
            catch (DurableObjectNotFoundException)
            {
                missingThrewNotFound = true;
            }

            Assert.True(missingThrewNotFound);

            // Activate and increment, then query via async path.
            await factory.Get<IAuditedCounter>(id).IncrementAsync(7);
            var count = await factory.QueryDurableObjectAsync<int>(id, "GetCount");

            Assert.Equal(7, count);

            await factory.Get<IAuditedCounter>(id).DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
