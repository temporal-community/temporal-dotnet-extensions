using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

/// <summary>
/// Tier-1 (resident) state lifecycle. An object increments to 5, then sits idle.
/// Because idle is a no-op in the resident tier (Tier 2 cold passivation excluded from v1),
/// the object stays open on the SAME run ID — no completion, no data loss, no churn.
/// </summary>
public sealed class IdleLifecycleTests : DurableObjectTestBase
{
    public IdleLifecycleTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task State_SurvivesIdleWindow_SameRunId()
    {
        const string id = "scenario-h-obj";
        var tq = UniqueTaskQueue();

        using var worker = TestWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(IdlingCounter)]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            var factory = TestFactory.Create(Client, tq);
            var counter = factory.Get<IIdlingCounter>(id);

            await counter.IncrementAsync(5);
            var runIdBefore = (await Client.GetWorkflowHandle(id).DescribeAsync()).RunId;

            // Idle for 2.5s — a real idle timeout would have fired if Tier 2 were active.
            await Task.Delay(TimeSpan.FromMilliseconds(2500));

            var count = counter.GetCount();
            var runIdAfter = (await Client.GetWorkflowHandle(id).DescribeAsync()).RunId;

            // Count did not reset — object stayed resident.
            Assert.Equal(5, count);
            // Same run ID — no CAN or completion occurred.
            Assert.Equal(runIdBefore, runIdAfter);

            await counter.DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
