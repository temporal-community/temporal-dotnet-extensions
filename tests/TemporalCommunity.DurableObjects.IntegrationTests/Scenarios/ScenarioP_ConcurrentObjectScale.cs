using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Scenarios;

/// <summary>
/// Activates a bounded set of independent object IDs concurrently and verifies that updates and
/// queries remain isolated. This is a regression check, not a throughput benchmark.
/// </summary>
public sealed class ScenarioP_ConcurrentObjectScale : DurableObjectTestBase
{
    public ScenarioP_ConcurrentObjectScale(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ConcurrentObjects_KeepIndependentState()
    {
        const int objectCount = 50;
        var taskQueue = UniqueTaskQueue();

        using var worker = ScenarioWorkerBuilder.Build(
            Client,
            taskQueue,
            workflowTypes: [typeof(AuditedCounter)]);

        using var cancellation = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(cancellation.Token);
        try
        {
            var factory = TestFactory.Create(Client, taskQueue);
            var objects = Enumerable.Range(1, objectCount)
                .Select(index => (Index: index, Client: factory.Get<IAuditedCounter>($"scale-{index}")))
                .ToArray();

            var results = await Task.WhenAll(objects.Select(item =>
                item.Client.IncrementAsync(item.Index)));

            Assert.Equal(Enumerable.Range(1, objectCount), results);
            Assert.All(objects, item => Assert.Equal(item.Index, item.Client.GetCount()));

            await Task.WhenAll(objects.Select(item => item.Client.DeactivateAsync()));
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await workerTask; } catch (OperationCanceledException) { }
        }
    }
}
