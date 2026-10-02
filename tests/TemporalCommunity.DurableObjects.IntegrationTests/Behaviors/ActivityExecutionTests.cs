using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

/// <summary>
/// A durable object performs external I/O only through an Activity.
/// Verifies that an update can call an Activity and return its result round-trip.
/// </summary>
public sealed class ActivityExecutionTests : DurableObjectTestBase
{
    public ActivityExecutionTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ActivityResult_RoundTripsCorrectly()
    {
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
            var counter = factory.Get<IAuditedCounter>("scenario-a-obj");

            var receipt = await counter.IncrementWithAuditAsync(5);
            var count = counter.GetCount();

            Assert.StartsWith("audit:scenario-a-obj:5:", receipt, StringComparison.Ordinal);
            Assert.Equal(5, count);

            await counter.DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
