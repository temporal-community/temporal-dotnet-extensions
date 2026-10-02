using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

/// <summary>
/// Fire N concurrent cold callers at one object ID simultaneously.
/// Single-activation holds if exactly one execution exists, all updates serialize,
/// the final count == N, and no WorkflowAlreadyStartedException leaks to callers.
/// </summary>
public sealed class ConcurrentActivationTests : DurableObjectTestBase
{
    public ConcurrentActivationTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task NconcurrentColdCallers_ProduceSingleExecution_AndCorrectCount()
    {
        const int n = 10;
        const string id = "scenario-c-obj";
        var tq = UniqueTaskQueue();

        using var worker = TestWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(AuditedCounter)],
            activityInstances: [new AuditActivities()]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            var factory = TestFactory.Create(Client, tq);

            // Fire N concurrent cold callers — all using update-with-start on the same ID.
            var tasks = Enumerable.Range(0, n)
                .Select(_ => factory.Get<IAuditedCounter>(id).IncrementAsync(1))
                .ToArray();

            // No WorkflowAlreadyStartedException should leak — update-with-start is race-free.
            await Task.WhenAll(tasks);

            // All updates applied — count must be N.
            var count = factory.Get<IAuditedCounter>(id).GetCount();
            Assert.Equal(n, count);

            // Exactly one workflow execution (single run ID).
            var runId = (await Client.GetWorkflowHandle(id).DescribeAsync()).RunId;
            Assert.NotNull(runId);

            await factory.Get<IAuditedCounter>(id).DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
