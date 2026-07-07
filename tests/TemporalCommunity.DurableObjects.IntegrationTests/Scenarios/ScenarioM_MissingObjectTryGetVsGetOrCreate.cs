using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Scenarios;

/// <summary>
/// Scenario M: Two explicit options for a read against a missing object.
/// (1) QueryOrDefaultAsync returns default without creating anything (side-effect-free try-get).
/// (2) GetOrCreateAsync materializes the object explicitly, after which reads succeed.
/// </summary>
public sealed class ScenarioM_MissingObjectTryGetVsGetOrCreate : DurableObjectTestBase
{
    public ScenarioM_MissingObjectTryGetVsGetOrCreate(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task TryGet_ReturnsDefault_WithoutMaterializing_ThenGetOrCreate_Materializes()
    {
        const string id = "scenario-m-obj";
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

            // (1) QueryOrDefaultAsync on a non-existent object returns default(int) = 0, without creating it.
            // [return: MaybeNull] on Task<int> — for value types default(int) = 0 is returned, not null.
            var tryGetResult = await factory.QueryOrDefaultAsync<int>(id, "GetCount");
            Assert.Equal(0, tryGetResult);

            // The object must still be absent — a direct query throws DurableObjectNotFoundException.
            var stillAbsent = false;
            try { await factory.QueryDurableObjectAsync<int>(id, "GetCount"); }
            catch (DurableObjectNotFoundException) { stillAbsent = true; }

            Assert.True(stillAbsent, "QueryOrDefaultAsync should not have materialized the object");

            // (2) GetOrCreateAsync materializes the object.
            var obj = await factory.GetOrCreateAsync<IAuditedCounter>(id);
            var afterCreate = obj.GetCount();

            Assert.Equal(0, afterCreate);

            await obj.DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
