using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

/// <summary>
/// Assembly scan registers correct types. Active objects of a type are enumerable
/// via ListDurableObjectsAsync (polled since visibility is eventually consistent).
/// </summary>
public sealed class WorkflowRegistrationTests : DurableObjectTestBase
{
    public WorkflowRegistrationTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task AssemblyScan_RegistersCorrectTypes_AndListReturnsActiveInstances()
    {
        var tq = UniqueTaskQueue();

        // Use assembly scan on the IntegrationTests assembly — finds AuditedCounter and other
        // concrete DurableObjectBase subclasses. Note: CounterWithSignal is in the UNIT test
        // assembly, not here, so the scan in the integration tests is clean.
        var options = new TemporalCommunity.DurableObjects.DurableObjectWorkerOptions();
        var workerOptions = new Temporalio.Worker.TemporalWorkerOptions(tq);
        // Register by assembly scan of this test assembly (integration tests).
        workerOptions.AddDurableObjectWorkflows(typeof(AuditedCounter).Assembly, options);
        workerOptions.AddAllActivities(new AuditActivities());

        using var worker = new Temporalio.Worker.TemporalWorker(Client, workerOptions);
        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            var factory = TestFactory.Create(Client, tq);
            var ids = new[] { "scenario-l-obj-1", "scenario-l-obj-2" };

            // Activate both objects.
            foreach (var id in ids)
            {
                await factory.Get<IAuditedCounter>(id).IncrementAsync(1);
            }

            // Poll visibility (eventually consistent — up to ~5s max).
            var found = new List<string>();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                found.Clear();
                await foreach (var id in factory.ListDurableObjectsAsync<IAuditedCounter>())
                {
                    found.Add(id);
                }

                if (ids.All(found.Contains)) break;
                await Task.Delay(250);
            }

            Assert.All(ids, id => Assert.Contains(id, found));

            // The rich API defaults to canonical running objects and uses the built-in
            // TemporalScheduledById visibility attribute instead of parsing workflow IDs.
            var executions = new List<DurableObjectExecutionInfo>();
            await foreach (var execution in
                           factory.ListDurableObjectExecutionsAsync<IAuditedCounter>())
            {
                if (ids.Contains(execution.ObjectId))
                {
                    executions.Add(execution);
                }
            }

            Assert.Equal(2, executions.Count);
            Assert.All(executions, execution =>
            {
                Assert.False(execution.IsScheduled);
                Assert.Equal(tq, execution.TaskQueue);
                Assert.False(string.IsNullOrWhiteSpace(execution.RunId));
            });

            // Clean up.
            foreach (var id in ids)
            {
                await factory.Get<IAuditedCounter>(id).DeactivateAsync();
            }
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
