using Temporalio.Client.Schedules;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Scenarios;

/// <summary>
/// Scenario F: A Temporal Schedule activates the ScheduledGreeter on a 2s interval.
/// Each tick creates a fresh time-suffixed execution that runs the Audit activity and self-deactivates.
/// Success = at least 2 schedule actions recorded.
/// </summary>
public sealed class ScenarioF_SchedulePeriodicActivation : DurableObjectTestBase
{
    public ScenarioF_SchedulePeriodicActivation(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Schedule_CreatesFreshExecutionPerTick()
    {
        var tq = UniqueTaskQueue();
        var scheduleId = $"sched-f-{Guid.NewGuid():N}";
        const string objectId = "scenario-f-obj";

        using var worker = ScenarioWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(ScheduledGreeter)],
            activityInstances: [new ScenarioActivities()]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        Temporalio.Client.Schedules.ScheduleHandle? schedHandle = null;
        try
        {
            var factory = TestFactory.Create(Client, tq);
            schedHandle = await factory.CreateDurableObjectScheduleAsync<IScheduledGreeter>(
                scheduleId,
                objectId,
                new ScheduleSpec
                {
                    Intervals = [new ScheduleIntervalSpec(TimeSpan.FromSeconds(2))],
                },
                taskQueue: tq,
                scheduleOptions: new ScheduleOptions { TriggerImmediately = true });

            // Wait for at least 2 schedule actions (immediate + 1 interval fire).
            await Task.Delay(TimeSpan.FromMilliseconds(6000));

            var desc = await schedHandle.DescribeAsync();
            var count = desc.Info.NumActions;

            Assert.True(count >= 2, $"Expected >= 2 schedule actions but got {count}");

            DurableObjectExecutionInfo? scheduledExecution = null;
            for (var attempt = 0; attempt < 20 && scheduledExecution is null; attempt++)
            {
                await foreach (var execution in factory.ListDurableObjectExecutionsAsync<IScheduledGreeter>(
                                   new DurableObjectListOptions(
                                       runningOnly: false,
                                       includeScheduled: true)))
                {
                    if (execution.ScheduleId == scheduleId)
                    {
                        scheduledExecution = execution;
                        break;
                    }
                }

                if (scheduledExecution is null)
                {
                    await Task.Delay(250);
                }
            }

            Assert.NotNull(scheduledExecution);
            Assert.True(scheduledExecution.IsScheduled);
            Assert.Equal(scheduleId, scheduledExecution.ScheduleId);
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
