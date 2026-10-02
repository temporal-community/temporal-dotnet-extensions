using Temporalio.Worker;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

/// <summary>
/// Start a slow update (increment + durable timer), kill the worker while it is
/// parked, then bring a fresh worker up. The update must complete after restart with the
/// correct incremented value, proving replay/recovery.
/// </summary>
public sealed class WorkerRestartTests : DurableObjectTestBase
{
    public WorkerRestartTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Update_CompletesAfterWorkerRestart()
    {
        const string id = "scenario-d-obj";
        var tq = UniqueTaskQueue();

        // Worker 1: accept the slow update.
        var cts1 = new CancellationTokenSource();
        var worker1 = BuildWorker(tq);
        var run1 = worker1.ExecuteAsync(cts1.Token);

        var counter = TestFactory.Create(Client, tq).Get<IAuditedCounter>(id);

        // Enter through the DurableObjects public API. The update mutates state before parking
        // on a durable timer, so observing 7 proves worker 1 started the handler.
        var updateTask = counter.IncrementSlowlyAsync(7);
        var startedDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var observedCount = 0;
        while (DateTime.UtcNow < startedDeadline)
        {
            try
            {
                observedCount = counter.GetCount();
                if (observedCount == 7)
                {
                    break;
                }
            }
            catch (DurableObjectNotFoundException)
            {
                // Update-with-start has not made the cold object queryable yet.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(7, observedCount);

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
            var result = await updateTask;
            var count = counter.GetCount();

            Assert.Equal(7, result);
            Assert.Equal(7, count);

            // Clean up.
            await counter.DeactivateAsync();
        }
        finally
        {
            await cts2.CancelAsync();
            try { await run2; } catch (OperationCanceledException) { }
        }
    }

    private TemporalWorker BuildWorker(string tq) => TestWorkerBuilder.Build(
        Client, tq,
        workflowTypes: [typeof(AuditedCounter)],
        activityInstances: [new AuditActivities()]);
}
