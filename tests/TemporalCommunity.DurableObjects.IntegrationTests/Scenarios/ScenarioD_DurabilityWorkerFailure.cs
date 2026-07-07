using Temporalio.Client;
using Temporalio.Worker;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Scenarios;

/// <summary>
/// Scenario D: Start a slow update (increment + durable timer), kill the worker while it is
/// parked, then bring a fresh worker up. The update must complete after restart with the
/// correct incremented value, proving replay/recovery.
/// </summary>
public sealed class ScenarioD_DurabilityWorkerFailure : DurableObjectTestBase
{
    public ScenarioD_DurabilityWorkerFailure(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Update_CompletesAfterWorkerRestart()
    {
        const string id = "scenario-d-obj";
        var tq = UniqueTaskQueue();

        // Worker 1: accept the slow update.
        var cts1 = new CancellationTokenSource();
        var worker1 = BuildWorker(tq);
        var run1 = worker1.ExecuteAsync(cts1.Token);

        // Cold-start the workflow.
        var handle = await Client.StartWorkflowAsync(
            "AuditedCounter",
            Array.Empty<object?>(),
            new WorkflowOptions(id: id, taskQueue: tq)
            {
                IdConflictPolicy = Temporalio.Api.Enums.V1.WorkflowIdConflictPolicy.UseExisting,
            });

        // Start the slow update (increment + 4s durable timer) and wait for acceptance only.
        var updateHandle = await handle.StartUpdateAsync<int>(
            "IncrementSlowly",
            [7],
            new WorkflowUpdateStartOptions(WorkflowUpdateStage.Accepted));

        // Kill worker 1 while the timer is still pending.
        await cts1.CancelAsync();
        try { await run1; } catch (OperationCanceledException) { }
        worker1.Dispose();

        // Worker 2: resume with fresh worker on the same task queue.
        var cts2 = new CancellationTokenSource();
        using var worker2 = BuildWorker(tq);
        var run2 = worker2.ExecuteAsync(cts2.Token);
        try
        {
            // The update should complete via replay on worker 2.
            var result = await updateHandle.GetResultAsync();
            var count = await handle.QueryAsync<int>("GetCount", Array.Empty<object?>());

            Assert.Equal(7, result);
            Assert.Equal(7, count);

            // Clean up.
            await handle.ExecuteUpdateAsync<object?>("Deactivate", Array.Empty<object?>());
        }
        finally
        {
            await cts2.CancelAsync();
            try { await run2; } catch (OperationCanceledException) { }
        }
    }

    private TemporalWorker BuildWorker(string tq) => ScenarioWorkerBuilder.Build(
        Client, tq,
        workflowTypes: [typeof(AuditedCounter)],
        activityInstances: [new ScenarioActivities()]);
}
