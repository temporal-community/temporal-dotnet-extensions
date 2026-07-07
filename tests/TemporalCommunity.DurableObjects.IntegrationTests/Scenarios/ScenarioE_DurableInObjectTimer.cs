using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Scenarios;

/// <summary>
/// Scenario E: A recurring in-object timer increments a counter every second. The object must
/// stay resident (no idle-triggered CAN or passivation) so the count can climb. Success = ticks
/// count >= 3 after ~3.5 seconds.
/// </summary>
public sealed class ScenarioE_DurableInObjectTimer : DurableObjectTestBase
{
    public ScenarioE_DurableInObjectTimer(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task RecurringTimer_FiresDurably_ObjectStaysResident()
    {
        const string id = "scenario-e-obj";
        var tq = UniqueTaskQueue();

        using var worker = ScenarioWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(TimerCounter)]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            // Start the TimerCounter workflow directly (no update needed — timer fires on activate).
            var handle = await Client.StartWorkflowAsync(
                "TimerCounter",
                Array.Empty<object?>(),
                new Temporalio.Client.WorkflowOptions(id: id, taskQueue: tq)
                {
                    IdConflictPolicy = Temporalio.Api.Enums.V1.WorkflowIdConflictPolicy.UseExisting,
                });

            // Wait ~3.5s — timer fires every 1s so we expect at least 3 ticks.
            await Task.Delay(TimeSpan.FromMilliseconds(3500));

            var ticks = await handle.QueryAsync<int>("GetTicks", Array.Empty<object?>());

            Assert.True(ticks >= 3, $"Expected >= 3 ticks but got {ticks}");

            // Deactivate cleanly.
            await handle.ExecuteUpdateAsync<object?>("Deactivate", Array.Empty<object?>());
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
