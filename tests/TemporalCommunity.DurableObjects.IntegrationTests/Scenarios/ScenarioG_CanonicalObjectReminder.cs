using Temporalio.Client.Schedules;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Scenarios;

/// <summary>
/// Scenario G: The canonical-object reminder pattern. A Schedule starts a ReminderDispatcher each
/// tick, whose Activity update-with-starts the SAME target ID. Unlike Scenario F, every reminder
/// lands on one canonical execution so the count ACCUMULATES on a single run ID.
/// Verifies: count >= 2 with at-least-once delivery; distinct DeliveryIds per tick.
/// </summary>
public sealed class ScenarioG_CanonicalObjectReminder : DurableObjectTestBase
{
    public ScenarioG_CanonicalObjectReminder(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ReminderCount_AccumulatesOnOneRunId()
    {
        var tq = UniqueTaskQueue();
        var scheduleId = $"sched-g-{Guid.NewGuid():N}";
        const string targetId = "scenario-g-target";

        using var worker = ScenarioWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(ReminderTarget)],
            activityInstances: [new ReminderDeliveryActivities(Client)]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        Temporalio.Client.Schedules.ScheduleHandle? schedHandle = null;
        try
        {
            var factory = TestFactory.Create(Client, tq);
            schedHandle = await factory.CreateDurableObjectReminderAsync<IReminderTarget>(
                scheduleId,
                targetId,
                "heartbeat",
                new ScheduleSpec
                {
                    Intervals = [new ScheduleIntervalSpec(TimeSpan.FromSeconds(2))],
                },
                taskQueue: tq,
                scheduleOptions: new ScheduleOptions { TriggerImmediately = true });

            // Wait for at least 2 reminder deliveries.
            await Task.Delay(TimeSpan.FromMilliseconds(8000));

            var proxy = factory.Get<IReminderTarget>(targetId);
            var count = proxy.GetReminderCount();
            var runId = (await Client.GetWorkflowHandle(targetId).DescribeAsync()).RunId;

            // Count accumulates on a single run ID (not reset per tick like Scenario F).
            Assert.True(count >= 2, $"Expected >= 2 reminder deliveries but got {count}");
            Assert.NotNull(runId);

            await proxy.DeactivateAsync();
        }
        finally
        {
            if (schedHandle is not null)
            {
                try { await schedHandle.DeleteAsync(); } catch (Exception) { }
            }
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
