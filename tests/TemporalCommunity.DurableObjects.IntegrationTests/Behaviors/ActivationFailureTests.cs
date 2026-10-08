using Temporalio.Exceptions;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

/// <summary>
/// OnActivateAsync throw terminates the workflow. The next update-with-start on the
/// same object ID starts a fresh clean execution (count == 0, new run ID). This is the
/// "terminated object is recoverable" invariant.
/// </summary>
public sealed class ActivationFailureTests : DurableObjectTestBase
{
    public ActivationFailureTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ActivationFailure_TerminatesWorkflow_NextStartCreatesCleanExecution()
    {
        // Use a unique object ID so the static FailedOnce dict is fresh per test run.
        var id = $"scenario-o-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barrier = new Slice3Barrier();

        using var worker = TestWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(ActivationFailureCounter)],
            activityInstances: [new Slice3BarrierActivities(barrier)]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            var factory = TestFactory.Create(Client, tq);

            // First activation: OnActivateAsync throws → workflow terminates with ActivationFailure.
            // The exception propagates from DeactivateAsync (or any update) as a failure.
            // The proxy catches this as WorkflowUpdateFailedException from the terminated workflow.
            // We call GetOrCreateAsync first to ensure the workflow starts, then check its state.
            var firstProxy = await factory.GetOrCreateAsync<IActivationFailureCounter>(id);

            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var pendingUpdate = Client.GetWorkflowHandle(id).ExecuteUpdateAsync<object?>(
                "Deactivate", Array.Empty<object?>());
            Assert.False(pendingUpdate.IsCompleted);

            barrier.Release.TrySetResult();
            await Assert.ThrowsAsync<WorkflowUpdateFailedException>(
                () => pendingUpdate.WaitAsync(TimeSpan.FromSeconds(20)));

            // The first workflow execution should be terminated. Get its run ID before it closes.
            var firstDesc = await Client.GetWorkflowHandle(id).DescribeAsync();
            var firstRunId = firstDesc.RunId;

            // Wait for the terminated workflow to be in a closed state.
            // Poll until the execution is no longer Running.
            for (var i = 0; i < 30; i++)
            {
                var desc2 = await Client.GetWorkflowHandle(id).DescribeAsync();
                if (desc2.Status != Temporalio.Api.Enums.V1.WorkflowExecutionStatus.Running) break;
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }

            // Second activation: GetOrCreateAsync on the same ID starts a fresh execution.
            // ActivationFailureCounter.FailedOnce now contains the first workflow ID, so
            // a new execution (different workflow run ID but same workflow ID) will succeed.
            // NOTE: Temporal's IdReusePolicy.AllowDuplicate (set explicitly in GetOrCreateAsync)
            // permits starting a new execution after the previous one terminated.
            var secondProxy = await factory.GetOrCreateAsync<IActivationFailureCounter>(id);

            // Wait for the new execution to activate successfully.
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            var secondDesc = await Client.GetWorkflowHandle(id).DescribeAsync();
            var secondRunId = secondDesc.RunId;

            // New run ID: fresh execution, not the terminated one.
            Assert.NotEqual(firstRunId, secondRunId);

            // Count is 0: clean state, not contaminated by the failed first execution.
            var count = secondProxy.GetCount();
            Assert.Equal(0, count);

            await secondProxy.DeactivateAsync();
        }
        finally
        {
            barrier.Release.TrySetResult();
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
