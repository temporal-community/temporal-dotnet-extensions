using System.Diagnostics;
using Temporalio.Activities;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Worker.Interceptors;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

public sealed class Slice3AcceptanceTests : DurableObjectTestBase
{
    private static readonly TimeSpan EvidenceTimeout = TimeSpan.FromSeconds(20);

    public Slice3AcceptanceTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task BlockedActivation_UpdateWaitsAndQueryFailsNotReady()
    {
        var id = $"slice2-activation-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barrier = new Slice3Barrier();
        using var worker = TestWorkerBuilder.Build(
            Client,
            tq,
            workflowTypes: [typeof(BlockedActivationObject)],
            activityInstances: [new Slice3BarrierActivities(barrier)]);
        using var workerCts = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(workerCts.Token);
        var handle = Client.GetWorkflowHandle(id);

        try
        {
            handle = await Client.StartWorkflowAsync(
                nameof(BlockedActivationObject),
                Array.Empty<object?>(),
                new WorkflowOptions(id, tq));

            await barrier.Reached.Task.WaitAsync(EvidenceTimeout);

            var updateObservation = handle.ExecuteUpdateAsync<string>(
                "ObserveUpdate", Array.Empty<object?>());
            var queryFailure = await Assert.ThrowsAsync<WorkflowQueryFailedException>(
                () => handle.QueryAsync<string>("ReadPhase", Array.Empty<object?>()));
            Assert.Contains(
                "Object is not ready: activation is incomplete.",
                queryFailure.Message,
                StringComparison.Ordinal);
            Assert.False(updateObservation.IsCompleted);

            barrier.Release.TrySetResult();
            Assert.Equal("update:active", await updateObservation);
            await WaitForQueryValueAsync(handle, "ReadPhase", "active", id);
            await handle.ExecuteUpdateAsync<object?>("Deactivate", Array.Empty<object?>());
        }
        finally
        {
            barrier.Release.TrySetResult();
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(workerCts, workerTask);
        }
    }

    [Fact]
    public async Task BlockedSnapshotPreparation_RejectsNewUpdateBeforeHandler()
    {
        var id = $"slice2-snapshot-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barriers = new Slice3CanBarriers();
        using var worker = TestWorkerBuilder.Build(
            Client,
            tq,
            workflowTypes: [typeof(SnapshotAdmissionObject)],
            activityInstances: [new Slice3CanBarrierActivities(barriers)]);
        using var workerCts = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(workerCts.Token);
        var handle = Client.GetWorkflowHandle(id);

        try
        {
            handle = await Client.StartWorkflowAsync(
                nameof(SnapshotAdmissionObject),
                [null],
                new WorkflowOptions(id, tq));
            var firstRunId = (await handle.DescribeAsync()).RunId;

            var enteredUpdate = handle.ExecuteUpdateAsync<int>(
                "EnterAndRequestContinueAsNew", [5]);
            await barriers.EnteredUpdateReached.Task.WaitAsync(EvidenceTimeout);

            var rejectedUpdate = handle.ExecuteUpdateAsync<int>("AddDuringSnapshot", [7]);
            Assert.False(rejectedUpdate.IsCompleted);

            var rejection = await Assert.ThrowsAsync<WorkflowUpdateFailedException>(
                () => rejectedUpdate.WaitAsync(EvidenceTimeout));
            var applicationFailure = Assert.IsType<ApplicationFailureException>(
                rejection.InnerException);
            Assert.Equal("ObjectContinuingAsNew", applicationFailure.ErrorType);
            Assert.Equal(
                "Update 'AddDuringSnapshot' rejected: object is continuing as new.",
                applicationFailure.Message);
            Assert.True(applicationFailure.NonRetryable);
            Assert.False(barriers.SnapshotReached.Task.IsCompleted);

            barriers.ReleaseEnteredUpdate.TrySetResult();
            Assert.Equal(5, await enteredUpdate.WaitAsync(EvidenceTimeout));
            await barriers.SnapshotReached.Task.WaitAsync(EvidenceTimeout);

            barriers.ReleaseSnapshot.TrySetResult();
            var continuedRunId = await WaitForRunIdChangeAsync(handle, firstRunId, id);

            Assert.NotEqual(firstRunId, continuedRunId);
            Assert.Equal(5, await handle.QueryAsync<int>("ReadCount", Array.Empty<object?>()));
            await handle.ExecuteUpdateAsync<object?>("Deactivate", Array.Empty<object?>());
        }
        finally
        {
            barriers.ReleaseEnteredUpdate.TrySetResult();
            barriers.ReleaseSnapshot.TrySetResult();
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(workerCts, workerTask);
        }
    }

    [Fact]
    public async Task ReminderRetryAcrossContinueAsNew_ReusesIdentityAndHasOneBusinessEffect()
    {
        var tq = UniqueTaskQueue();
        var targetId = $"slice2-reminder-target-{Guid.NewGuid():N}";
        var dispatcherId = $"slice2-reminder-dispatch-{Guid.NewGuid():N}";
        const string reminderName = "retry-across-rollover";
        var retryBarrier = new ReminderRetryBarrier();
        var deliveryActivity = new RetryAfterSuccessfulReminderActivity(Client, retryBarrier);
        using var worker = TestWorkerBuilder.Build(
            Client,
            tq,
            workflowTypes: [typeof(ReminderRetryObject)],
            activityInstances: [deliveryActivity]);
        using var workerCts = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(workerCts.Token);
        var targetHandle = Client.GetWorkflowHandle(targetId);
        var dispatcherHandle = Client.GetWorkflowHandle(dispatcherId);

        try
        {
            dispatcherHandle = await Client.StartWorkflowAsync(
                "ReminderDispatcher",
                [new ReminderDispatch(
                    targetId,
                    nameof(ReminderRetryObject),
                    tq,
                    reminderName)],
                new WorkflowOptions(dispatcherId, tq));

            await retryBarrier.FirstAttemptDelivered.Task.WaitAsync(EvidenceTimeout);
            var firstState = await WaitForReminderAttemptsAsync(targetHandle, 1, targetId);
            var continuedRunId = await WaitForRunIdChangeAsync(
                targetHandle, firstState.FirstRunId, targetId);

            retryBarrier.ReleaseFirstAttempt.TrySetResult();
            await retryBarrier.SecondAttemptDelivered.Task.WaitAsync(EvidenceTimeout);
            await dispatcherHandle.GetResultAsync();

            var finalState = await WaitForReminderAttemptsAsync(targetHandle, 2, targetId);
            var expectedUpdateId = $"{dispatcherId}:{targetId}:{reminderName}";

            Assert.NotEqual(firstState.FirstRunId, continuedRunId);
            Assert.Equal(2, finalState.DeliveryAttempts);
            Assert.Equal(1, finalState.BusinessEffects);
            Assert.Equal(dispatcherId, finalState.DeliveryId);
            Assert.Equal(expectedUpdateId, deliveryActivity.ObservedUpdateId);

            await targetHandle.ExecuteUpdateAsync<object?>("Deactivate", Array.Empty<object?>());
        }
        finally
        {
            retryBarrier.ReleaseFirstAttempt.TrySetResult();
            await TerminateIfRunningAsync(dispatcherHandle, dispatcherId);
            await TerminateIfRunningAsync(targetHandle, targetId);
            await StopWorkerAsync(workerCts, workerTask);
        }
    }

    [Fact]
    public async Task LifecycleCancellation_TerminalStatusesAreCanceledCanceledCompleted()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var activationId = $"slice2-activation-cancel-{suffix}";
        var snapshotId = $"slice2-snapshot-cancel-{suffix}";
        var deactivationId = $"slice2-deactivation-cancel-{suffix}";
        var tq = UniqueTaskQueue();
        using var worker = TestWorkerBuilder.Build(
            Client, tq, workflowTypes: [typeof(LifecycleTerminalStatusObject)]);
        using var workerCts = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(workerCts.Token);
        var activation = Client.GetWorkflowHandle(activationId);
        var snapshot = Client.GetWorkflowHandle(snapshotId);
        var deactivation = Client.GetWorkflowHandle(deactivationId);

        try
        {
            activation = await StartLifecycleObjectAsync(activationId, tq);
            await WaitForQueryFailureAsync(
                activation,
                "ReadPhase",
                "Object is not ready: activation is incomplete.",
                activationId);
            var pendingActivationUpdate = activation.ExecuteUpdateAsync<object?>(
                "PrepareSnapshot", Array.Empty<object?>());
            Assert.False(pendingActivationUpdate.IsCompleted);
            await activation.CancelAsync();
            var pendingFailure = await Assert.ThrowsAnyAsync<Exception>(
                () => pendingActivationUpdate.WaitAsync(EvidenceTimeout));
            Assert.True(
                pendingFailure is WorkflowUpdateFailedException or WorkflowFailedException,
                $"Expected an update or execution failure, got {pendingFailure.GetType().Name}.");

            snapshot = await StartLifecycleObjectAsync(snapshotId, tq);
            await snapshot.ExecuteUpdateAsync<object?>(
                "PrepareSnapshot", Array.Empty<object?>());
            await WaitForQueryValueAsync(snapshot, "ReadPhase", "snapshot-blocked", snapshotId);
            await snapshot.CancelAsync();

            deactivation = await StartLifecycleObjectAsync(deactivationId, tq);
            await deactivation.ExecuteUpdateAsync<object?>("Deactivate", Array.Empty<object?>());
            await WaitForQueryValueAsync(
                deactivation, "ReadPhase", "deactivation-blocked", deactivationId);
            await deactivation.CancelAsync();

            var activationStatus = await WaitForStatusAsync(
                activation, WorkflowExecutionStatus.Canceled, activationId);
            var snapshotStatus = await WaitForStatusAsync(
                snapshot, WorkflowExecutionStatus.Canceled, snapshotId);
            var deactivationStatus = await WaitForStatusAsync(
                deactivation, WorkflowExecutionStatus.Completed, deactivationId);

            Assert.Equal(WorkflowExecutionStatus.Canceled, activationStatus);
            Assert.Equal(WorkflowExecutionStatus.Canceled, snapshotStatus);
            Assert.Equal(WorkflowExecutionStatus.Completed, deactivationStatus);

            var activationFailure = await Assert.ThrowsAsync<WorkflowFailedException>(
                () => activation.GetResultAsync());
            var snapshotFailure = await Assert.ThrowsAsync<WorkflowFailedException>(
                () => snapshot.GetResultAsync());
            Assert.IsType<CanceledFailureException>(activationFailure.InnerException);
            Assert.IsType<CanceledFailureException>(snapshotFailure.InnerException);
            await deactivation.GetResultAsync();
        }
        finally
        {
            await TerminateIfRunningAsync(activation, activationId);
            await TerminateIfRunningAsync(snapshot, snapshotId);
            await TerminateIfRunningAsync(deactivation, deactivationId);
            await StopWorkerAsync(workerCts, workerTask);
        }
    }

    [Fact]
    public async Task AuthorizationCallbackException_FailsUpdateWithoutWedgingObject()
    {
        var id = $"slice3-auth-callback-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        bool Authorize(HandleUpdateInput input) =>
            input.Update == "ThrowAuthorization"
                ? throw new InvalidOperationException("sensitive authorization detail")
                : true;
        using var worker = TestWorkerBuilder.Build(
            Client,
            tq,
            workflowTypes: [typeof(AuthorizationCallbackObject)],
            options: new DurableObjectWorkerOptions { Authorize = Authorize });
        using var workerCts = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(workerCts.Token);
        var handle = Client.GetWorkflowHandle(id);

        try
        {
            handle = await Client.StartWorkflowAsync(
                nameof(AuthorizationCallbackObject),
                Array.Empty<object?>(),
                new WorkflowOptions(id, tq));

            var rejection = await Assert.ThrowsAsync<WorkflowUpdateFailedException>(
                () => handle.ExecuteUpdateAsync<object?>(
                    "ThrowAuthorization", Array.Empty<object?>()));
            var applicationFailure = Assert.IsType<ApplicationFailureException>(
                rejection.InnerException);
            Assert.Equal("AuthorizationFailure", applicationFailure.ErrorType);
            Assert.Equal(
                "Update 'ThrowAuthorization' rejected: authorization callback failed.",
                applicationFailure.Message);
            Assert.DoesNotContain(
                "sensitive authorization detail",
                applicationFailure.ToString(),
                StringComparison.Ordinal);
            Assert.True(applicationFailure.NonRetryable);

            Assert.Equal(
                1,
                await handle.ExecuteUpdateAsync<int>("Increment", Array.Empty<object?>()));
            await handle.ExecuteUpdateAsync<object?>("Deactivate", Array.Empty<object?>());
        }
        finally
        {
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(workerCts, workerTask);
        }
    }

    private Task<WorkflowHandle> StartLifecycleObjectAsync(string id, string taskQueue) =>
        Client.StartWorkflowAsync(
            nameof(LifecycleTerminalStatusObject),
            Array.Empty<object?>(),
            new WorkflowOptions(id, taskQueue));

    private static async Task WaitForQueryValueAsync(
        WorkflowHandle handle,
        string query,
        string expected,
        string failureId)
    {
        var timer = Stopwatch.StartNew();
        Exception? lastFailure = null;
        while (timer.Elapsed < EvidenceTimeout)
        {
            try
            {
                var value = await handle.QueryAsync<string>(query, Array.Empty<object?>());
                if (StringComparer.Ordinal.Equals(value, expected))
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is RpcException or WorkflowQueryFailedException)
            {
                lastFailure = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            $"[{failureId}] Query '{query}' did not reach '{expected}'.",
            lastFailure);
    }

    internal static async Task<string> WaitForRunIdChangeAsync(
        WorkflowHandle handle,
        string initialRunId,
        string failureId)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < EvidenceTimeout)
        {
            var currentRunId = (await handle.DescribeAsync()).RunId;
            if (!StringComparer.Ordinal.Equals(currentRunId, initialRunId))
            {
                return currentRunId;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            $"[{failureId}] Run ID did not change from '{initialRunId}'.");
    }

    private static async Task WaitForQueryFailureAsync(
        WorkflowHandle handle,
        string query,
        string expectedMessage,
        string failureId)
    {
        var timer = Stopwatch.StartNew();
        Exception? lastFailure = null;
        while (timer.Elapsed < EvidenceTimeout)
        {
            try
            {
                await handle.QueryAsync<string>(query, Array.Empty<object?>());
            }
            catch (WorkflowQueryFailedException ex)
                when (ex.Message.Contains(expectedMessage, StringComparison.Ordinal))
            {
                return;
            }
            catch (Exception ex) when (ex is RpcException or WorkflowQueryFailedException)
            {
                lastFailure = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            $"[{failureId}] Query '{query}' did not fail with '{expectedMessage}'.",
            lastFailure);
    }

    private static async Task<ReminderRetryState> WaitForReminderAttemptsAsync(
        WorkflowHandle handle,
        int expectedAttempts,
        string failureId)
    {
        var timer = Stopwatch.StartNew();
        Exception? lastFailure = null;
        while (timer.Elapsed < EvidenceTimeout)
        {
            try
            {
                var state = await handle.QueryAsync<ReminderRetryState>(
                    "ReadState", Array.Empty<object?>());
                if (state.DeliveryAttempts == expectedAttempts)
                {
                    return state;
                }
            }
            catch (Exception ex) when (ex is RpcException or WorkflowQueryFailedException)
            {
                lastFailure = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            $"[{failureId}] Reminder attempts did not reach {expectedAttempts}.",
            lastFailure);
    }

    internal static async Task<WorkflowExecutionStatus> WaitForStatusAsync(
        WorkflowHandle handle,
        WorkflowExecutionStatus expected,
        string failureId)
    {
        var timer = Stopwatch.StartNew();
        WorkflowExecutionStatus lastStatus = default;
        while (timer.Elapsed < EvidenceTimeout)
        {
            lastStatus = (await handle.DescribeAsync()).Status;
            if (lastStatus == expected)
            {
                return lastStatus;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            $"[{failureId}] Expected status {expected}, last observed {lastStatus}.");
    }

    internal static async Task TerminateIfRunningAsync(WorkflowHandle handle, string failureId)
    {
        try
        {
            if ((await handle.DescribeAsync()).Status == WorkflowExecutionStatus.Running)
            {
                await handle.TerminateAsync($"Slice 2 test cleanup: {failureId}");
            }
        }
        catch (RpcException)
        {
            // The workflow may never have started, or may have closed between describe/terminate.
        }
    }

    internal static async Task StopWorkerAsync(
        CancellationTokenSource workerCts,
        Task workerTask)
    {
        await workerCts.CancelAsync();
        try
        {
            await workerTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class ReminderRetryBarrier
    {
        public TaskCompletionSource FirstAttemptDelivered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstAttempt { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondAttemptDelivered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RetryAfterSuccessfulReminderActivity(
        ITemporalClient client,
        ReminderRetryBarrier barrier)
    {
        private readonly ReminderDeliveryActivities inner = new(client);

        public string ObservedUpdateId { get; private set; } = string.Empty;

        [Activity("DeliverReminder")]
        public async Task DeliverReminderAsync(ReminderDispatch dispatch)
        {
            var context = ActivityExecutionContext.Current;
            var deliveryId = context.Info.WorkflowId
                ?? throw new InvalidOperationException("Expected dispatcher workflow identity.");
            ObservedUpdateId =
                $"{deliveryId}:{dispatch.TargetObjectId}:{dispatch.ReminderName}";

            await inner.DeliverReminderAsync(dispatch);

            if (context.Info.Attempt == 1)
            {
                barrier.FirstAttemptDelivered.TrySetResult();
                await barrier.ReleaseFirstAttempt.Task.WaitAsync(context.CancellationToken);
                throw new ApplicationFailureException(
                    "Forced retry after successful reminder delivery.",
                    errorType: "Slice2ForcedReminderRetry",
                    nonRetryable: false);
            }

            barrier.SecondAttemptDelivered.TrySetResult();
        }
    }
}
