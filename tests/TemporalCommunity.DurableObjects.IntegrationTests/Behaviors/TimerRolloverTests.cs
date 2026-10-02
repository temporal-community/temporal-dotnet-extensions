using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

/// <summary>
/// Confirms a pending timer is re-armed from OnActivateAsync after Continue-as-New. Timer
/// registrations themselves are not carried in the framework's Continue-as-New arguments.
/// </summary>
public sealed class TimerRolloverTests : DurableObjectTestBase
{
    public TimerRolloverTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task PendingTimerIsRearmedAfterContinueAsNew()
    {
        const string id = "scenario-q-timer-rollover";
        var tq = UniqueTaskQueue();
        using var worker = TestWorkerBuilder.Build(
            Client, tq, workflowTypes: [typeof(TimerRolloverCounter)]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            var factory = TestFactory.Create(Client, tq);
            var counter = factory.Get<ITimerRolloverCounter>(id);
            var firstRunId = string.Empty;

            for (var i = 0; i < 20; i++)
            {
                await counter.IncrementAsync();
                var currentRunId = (await Client.GetWorkflowHandle(id).DescribeAsync()).RunId;
                firstRunId = firstRunId.Length == 0 ? currentRunId : firstRunId;
                if (currentRunId != firstRunId)
                {
                    break;
                }
            }

            var continuedRunId = (await Client.GetWorkflowHandle(id).DescribeAsync()).RunId;
            Assert.NotEqual(firstRunId, continuedRunId);

            await Task.Delay(TimeSpan.FromSeconds(4));
            Assert.Equal(1, (await factory.QueryDurableObjectAsync<TimerRolloverState>(id, "ReadTimerState")).Ticks);
            await counter.DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
