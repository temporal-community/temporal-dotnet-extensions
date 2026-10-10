#pragma warning disable CA1861 // Small expected arrays keep test assertions readable.
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Worker;
using TemporalCommunity.DurableObjects.Examples;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;
using static TemporalCommunity.DurableObjects.IntegrationTests.Behaviors.Slice3AcceptanceTests;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

public sealed class SignalSupportTests(WorkflowEnvironmentFixture fixture) : DurableObjectTestBase(fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingSignalAuthorization_FailsClosedForManualAndLateRegistration(bool late)
    {
        await WithGuardObjectAsync(typeof(RuntimeSignalGuardObject), late, _ => true, null,
            async (handle, logs) =>
            {
                await handle.SignalAsync("Named", []);
                await PollAsync(() => Task.FromResult(logs.Drops.Count == 1));
                Assert.Equal("SignalAuthorizationNotConfigured", Assert.Single(logs.Drops).Fields["Category"]);
                Assert.Equal(0, await handle.QueryAsync<int>("ReadCount", []));
                // Updates remain authorized and usable.
                await handle.ExecuteUpdateAsync<object?>("InstallDynamic", []);
            });
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SelectedDynamicHandler_DropsBeforeAuthorizationOrUserCode(
        bool assignedAtRuntime, bool late, bool authorize)
    {
        var authCalls = 0;
        await WithGuardObjectAsync(
            assignedAtRuntime ? typeof(RuntimeSignalGuardObject) : typeof(DeclaredDynamicSignalGuardObject),
            late, authorize ? _ => true : null,
            authorize ? _ => { Interlocked.Increment(ref authCalls); return true; } : null,
            async (handle, logs) =>
            {
                if (assignedAtRuntime) await handle.ExecuteUpdateAsync<object?>("InstallDynamic", []);
                await handle.SignalAsync("unknown", []);
                await PollAsync(() => Task.FromResult(logs.Drops.Count == 1));
                var drop = Assert.Single(logs.Drops);
                Assert.Equal("DynamicSignalNotSupported", drop.Fields["Category"]);
                Assert.Equal(LogLevel.Error, drop.Level);
                Assert.Null(drop.Exception);
                Assert.Equal(0, authCalls);
                Assert.Equal(0, await handle.QueryAsync<int>("ReadCount", []));
                // A mixed definition's named handler remains supported.
                await handle.SignalAsync("Named", []);
                await PollAsync(async () => await handle.QueryAsync<int>("ReadCount", []) == 1);
                Assert.Equal(authorize ? 1 : 0, authCalls);
                Assert.Single(logs.Drops);
            });
    }

    private async Task WithGuardObjectAsync(
        Type type, bool late,
        Func<Temporalio.Worker.Interceptors.HandleUpdateInput, bool>? authorize,
        Func<Temporalio.Worker.Interceptors.HandleSignalInput, bool>? authorizeSignal,
        Func<WorkflowHandle, SignalLogs, Task> test)
    {
        var id = $"signal-guard-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        using var logs = new SignalLogs(false);
        using var loggerFactory = logs.Factory;
        var options = new TemporalWorkerOptions(tq) { LoggerFactory = logs.Factory };
        if (late)
        {
            options.AddDurableObjectWorkflows(typeof(DurableObjectBase).Assembly,
                new DurableObjectWorkerOptions { Authorize = authorize, AuthorizeSignal = authorizeSignal });
        }
        else
        {
            options.Interceptors = [new DurableObjectWorkerInterceptor(true, authorize, authorizeSignal)];
        }
        options.AddWorkflow(type); // Deliberately bypass startup validation.
        using var worker = new TemporalWorker(Client, options);
        using var cts = new CancellationTokenSource();
        var running = worker.ExecuteAsync(cts.Token);
        var handle = Client.GetWorkflowHandle(id);
        try
        {
            await Client.StartWorkflowAsync(type.Name, [], new WorkflowOptions(id, tq));
            await PollAsync(async () => await handle.QueryAsync<int>("ReadCount", []) == 0);
            await test(handle, logs);
        }
        finally
        {
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(cts, running);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColdSignalWithStart_AcknowledgesBeforeActivation_ThenWaits(bool generated)
    {
        var id = $"cold-signal-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barrier = new Slice3Barrier();
        using var worker = TestWorkerBuilder.Build(Client, tq,
            [typeof(ColdSignalObject)], [new Slice3BarrierActivities(barrier)]);
        using var cts = new CancellationTokenSource();
        var running = Task.CompletedTask;
        var handle = Client.GetWorkflowHandle(id);
        try
        {
            var factory = TestFactory.Create(Client, tq);
            if (generated)
                await factory.GetColdSignalObjectClient(id).AppendAsync(7).WaitAsync(Timeout);
            else
                await DurableObjectProxy<IColdSignalObject>.Create(Client, id, tq).AppendAsync(7).WaitAsync(Timeout);

            // Receipt does not require a worker polling this queue.
            await WaitForSignalRecordedAsync(handle, "Append");
            running = worker.ExecuteAsync(cts.Token);
            await barrier.Reached.Task.WaitAsync(Timeout);
            var failure = await Assert.ThrowsAsync<WorkflowQueryFailedException>(
                () => handle.QueryAsync<int>("ReadCount", []));
            Assert.Contains("activation is incomplete", failure.Message, StringComparison.Ordinal);
            barrier.Release.TrySetResult();
            await PollAsync(async () => await handle.QueryAsync<int>("ReadCount", []) == 7);
            Assert.Equal("active", await handle.QueryAsync<string>("ReadPhase", []));
            var firstRun = (await handle.DescribeAsync()).RunId;

            Task SendAsync(int amount) => generated
                ? factory.GetColdSignalObjectClient(id).AppendAsync(amount)
                : DurableObjectProxy<IColdSignalObject>.Create(Client, id, tq).AppendAsync(amount);
            await SendAsync(5);
            await PollAsync(async () => await handle.QueryAsync<int>("ReadCount", []) == 12);
            Assert.Equal(firstRun, (await handle.DescribeAsync()).RunId);
            await handle.ExecuteUpdateAsync<object?>("Deactivate", []);
            await handle.GetResultAsync().WaitAsync(Timeout);
            await SendAsync(2);
            await PollAsync(async () => await handle.QueryAsync<int>("ReadCount", []) == 2);
            Assert.NotEqual(firstRun, (await handle.DescribeAsync()).RunId);
        }
        finally
        {
            barrier.Release.TrySetResult();
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(cts, running);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task SignalAndUpdate_RespectSerializeAcrossAwaits(bool serialize) =>
        WithObjectAsync(async (handle, barriers, _) =>
        {
            var update = handle.ExecuteUpdateAsync<object?>("BlockUpdate", [false]);
            await barriers.EnteredUpdateReached.Task.WaitAsync(Timeout);
            await handle.SignalAsync("Append", [3]);
            await WaitForSignalRecordedAsync(handle, "Append");
            if (!serialize)
                await PollAsync(async () => (await ReadAsync(handle)).Count == 3);

            var blocked = await ReadAsync(handle);
            Assert.Equal(serialize ? 0 : 3, blocked.Count);
            Assert.Equal(serialize ? new[] { "update-start" } : ["update-start", "signal:3"], blocked.Order);
            barriers.ReleaseEnteredUpdate.TrySetResult();
            await update.WaitAsync(Timeout);
            await PollAsync(async () => (await ReadAsync(handle)).Order.Count == 3);
            Assert.Equal(serialize
                ? new[] { "update-start", "update-end", "signal:3" }
                : ["update-start", "signal:3", "update-end"], (await ReadAsync(handle)).Order);
        }, new DurableObjectWorkerOptions { Serialize = serialize });

    [Fact]
    public Task UpdateWaitsForSuspendedSignal_AndSignalsAreCountedInOrder() =>
        WithObjectAsync(async (handle, barriers, _) =>
        {
            await handle.SignalAsync("BlockSignal", [false]);
            await barriers.EnteredUpdateReached.Task.WaitAsync(Timeout);
            var admittedUpdate = await handle.StartUpdateAsync<int>("Add", [5],
                new WorkflowUpdateStartOptions { WaitForStage = WorkflowUpdateStage.Accepted });
            var update = admittedUpdate.GetResultAsync();
            for (var i = 1; i <= 25; i++) await handle.SignalAsync("Append", [i]);
            await WaitForSignalRecordedAsync(handle, "Append", 25);
            Assert.Equal(0, (await ReadAsync(handle)).Count);
            Assert.False(update.IsCompleted);
            barriers.ReleaseEnteredUpdate.TrySetResult();
            Assert.Equal(15, await update.WaitAsync(Timeout));
            await PollAsync(async () => (await ReadAsync(handle)).Count == 340);
            Assert.Equal(new[] { "signal-start", "signal-end" }.Concat(
                Enumerable.Range(1, 25).Select(i => $"signal:{i}")), (await ReadAsync(handle)).Order);
        });

    [Fact]
    public Task SignalDuringAsyncSnapshot_RepeatsSnapshot_AndPreservesAppliedState() =>
        WithObjectAsync(async (handle, barriers, logs) =>
        {
            var firstRun = (await handle.DescribeAsync()).RunId;
            await handle.ExecuteUpdateAsync<object?>("RequestSnapshot", []);
            await barriers.SnapshotReached.Task.WaitAsync(Timeout);
            await handle.SignalAsync("Append", [9]);
            await PollAsync(async () => (await ReadAsync(handle)).Count == 9);
            var rejection = await Assert.ThrowsAsync<WorkflowUpdateFailedException>(
                () => handle.ExecuteUpdateAsync<int>("Add", [100]));
            Assert.Equal("ObjectContinuingAsNew",
                Assert.IsType<ApplicationFailureException>(rejection.InnerException).ErrorType);
            barriers.ReleaseSnapshot.TrySetResult();
            await WaitForRunIdChangeAsync(handle, firstRun, handle.Id);
            var state = await ReadAsync(handle);
            Assert.Equal(9, state.Count);
            Assert.Equal(2, state.SnapshotPasses);
            Assert.Equal(new[] { "signal:9" }, state.Order);
            Assert.Empty(logs.Drops);
            var history = await Client.GetWorkflowHandle(handle.Id, firstRun).FetchHistoryAsync();
            var replayOptions = new WorkflowReplayerOptions
            {
                Interceptors = [new DurableObjectWorkerInterceptor()],
            };
            replayOptions.AddWorkflow<SignalObject>();
            var replay = await new WorkflowReplayer(replayOptions).ReplayWorkflowAsync(history);
            Assert.Null(replay.ReplayFailure);
        });

    [Fact]
    public Task RolloverDrainsInflightAndQueuedSignals_ButRejectsUpdates() =>
        WithObjectAsync(async (handle, barriers, _) =>
        {
            var firstRun = (await handle.DescribeAsync()).RunId;
            await handle.SignalAsync("BlockSignal", [true]);
            await barriers.EnteredUpdateReached.Task.WaitAsync(Timeout);
            await handle.SignalAsync("Append", [4]);
            await WaitForSignalRecordedAsync(handle, "Append");
            var rejection = await Assert.ThrowsAsync<WorkflowUpdateFailedException>(
                () => handle.ExecuteUpdateAsync<int>("Add", [100]));
            Assert.Equal("ObjectContinuingAsNew",
                Assert.IsType<ApplicationFailureException>(rejection.InnerException).ErrorType);
            Assert.Equal(0, (await ReadAsync(handle)).Count);
            barriers.ReleaseEnteredUpdate.TrySetResult();
            await WaitForRunIdChangeAsync(handle, firstRun, handle.Id);
            Assert.Equal(14, (await ReadAsync(handle)).Count);
            Assert.Equal(new[] { "signal-start", "signal-end", "signal:4" }, (await ReadAsync(handle)).Order);
        });

    [Theory]
    [InlineData("ordinary", "System.InvalidOperationException")]
    [InlineData("application", "Temporalio.Exceptions.ApplicationFailureException")]
    [InlineData("unrelated-cancel", "System.OperationCanceledException")]
    public Task HandlerFailure_LogsRedactedError_PreservesPartialMutation_AndObjectUsable(
        string kind, string expectedType) =>
        WithObjectAsync(async (handle, _, logs) =>
        {
            await handle.SignalAsync("Fail", [kind]);
            await PollAsync(() => Task.FromResult(logs.Drops.Count == 1));
            var drop = Assert.Single(logs.Drops);
            Assert.Equal("SignalHandlerFailure", drop.Fields["Category"]);
            Assert.Equal("Fail", drop.Fields["Signal"]);
            Assert.Equal(expectedType, drop.Fields["ErrorType"]);
            Assert.Equal(LogLevel.Error, drop.Level);
            Assert.Null(drop.Exception);
            Assert.DoesNotContain("secret", drop.Message, StringComparison.Ordinal);
            Assert.Equal(1, (await ReadAsync(handle)).Count);
            Assert.Equal(3, await handle.ExecuteUpdateAsync<int>("Add", [2]));
            Assert.Equal(WorkflowExecutionStatus.Running, (await handle.DescribeAsync()).Status);

            // Replay reconstructs partial state but does not emit framework drop diagnostics.
            await handle.ExecuteUpdateAsync<object?>("Deactivate", []);
            await handle.GetResultAsync();
            var history = await handle.FetchHistoryAsync();
            var before = logs.Drops.Count;
            var options = new WorkflowReplayerOptions
            {
                Interceptors = [new DurableObjectWorkerInterceptor()],
                LoggerFactory = logs.Factory,
            };
            options.AddWorkflow<SignalObject>();
            var replay = await new WorkflowReplayer(options).ReplayWorkflowAsync(history);
            Assert.Null(replay.ReplayFailure);
            Assert.Equal(before, logs.Drops.Count);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AuthorizationDenialOrError_DropsWithCorrectCategory_AndUpdateAuthPreserved(bool throws) =>
        WithObjectAsync(async (handle, _, logs) =>
        {
            await handle.SignalAsync("Append", [8]);
            await PollAsync(() => Task.FromResult(logs.Drops.Count == 1));
            var drop = Assert.Single(logs.Drops);
            Assert.Equal(throws ? "AuthorizationFailure" : "Unauthorized", drop.Fields["Category"]);
            Assert.Equal(throws ? LogLevel.Error : LogLevel.Warning, drop.Level);
            Assert.Null(drop.Exception);
            Assert.DoesNotContain("secret", drop.Message, StringComparison.Ordinal);
            Assert.Equal(0, (await ReadAsync(handle)).Count);
            Assert.Equal(2, await handle.ExecuteUpdateAsync<int>("Add", [2]));
            var rejected = await Assert.ThrowsAsync<WorkflowUpdateFailedException>(
                () => handle.ExecuteUpdateAsync<int>("Add", [-1]));
            Assert.Equal("Unauthorized",
                Assert.IsType<ApplicationFailureException>(rejected.InnerException).ErrorType);
        }, new DurableObjectWorkerOptions
        {
            Authorize = input => input.Update != "Add" || (int)input.Args[0]! >= 0,
            AuthorizeSignal = _ => throws ? throw new InvalidOperationException("secret-auth-message") : false,
        });

    [Fact]
    public Task Deactivation_DropsSignalWithWarning_QueriesContinue() =>
        WithObjectAsync(async (handle, barriers, logs) =>
        {
            await handle.ExecuteUpdateAsync<object?>("BeginDeactivation", []);
            await barriers.SnapshotReached.Task.WaitAsync(Timeout);
            await handle.SignalAsync("Append", [8]);
            await PollAsync(() => Task.FromResult(logs.Drops.Count == 1));
            var drop = Assert.Single(logs.Drops);
            Assert.Equal("ObjectDeactivating", drop.Fields["Category"]);
            Assert.Equal(LogLevel.Warning, drop.Level);
            Assert.Equal(0, (await ReadAsync(handle)).Count);
            barriers.ReleaseSnapshot.TrySetResult();
            await handle.GetResultAsync().WaitAsync(Timeout);
            Assert.Equal(WorkflowExecutionStatus.Completed, (await handle.DescribeAsync()).Status);
        });

    [Fact]
    public Task QueuedSignal_RechecksDeactivationAfterSerializationGate() =>
        WithObjectAsync(async (handle, barriers, logs) =>
        {
            await handle.SignalAsync("BlockThenDeactivate", []);
            await barriers.EnteredUpdateReached.Task.WaitAsync(Timeout);
            await handle.SignalAsync("Append", [8]);
            await WaitForSignalRecordedAsync(handle, "Append");
            barriers.ReleaseEnteredUpdate.TrySetResult();
            await barriers.SnapshotReached.Task.WaitAsync(Timeout);
            await PollAsync(() => Task.FromResult(logs.Drops.Count == 1));
            Assert.Equal("ObjectDeactivating", Assert.Single(logs.Drops).Fields["Category"]);
            Assert.Equal(0, (await ReadAsync(handle)).Count);
            barriers.ReleaseSnapshot.TrySetResult();
            await handle.GetResultAsync().WaitAsync(Timeout);
        });

    [Fact]
    public Task SelfDeactivation_DropsSignalsWhileCleanupAwaits() =>
        WithObjectAsync(async (handle, barriers, logs) =>
        {
            await handle.SignalAsync("BeginSelfDeactivation", []);
            await barriers.SnapshotReached.Task.WaitAsync(Timeout);
            await handle.SignalAsync("Append", [8]);
            await PollAsync(() => Task.FromResult(logs.Drops.Count == 1));
            Assert.Equal("ObjectDeactivating", Assert.Single(logs.Drops).Fields["Category"]);
            Assert.Equal(0, (await ReadAsync(handle)).Count);
            barriers.ReleaseSnapshot.TrySetResult();
            await handle.GetResultAsync().WaitAsync(Timeout);
        });

    [Fact]
    public async Task HandlerFailure_DoesNotRollbackCompletedActivityEffects()
    {
        var effects = new SignalEffectActivities();
        await WithObjectAsync(async (handle, _, logs) =>
        {
            await handle.SignalAsync("EffectThenFail", []);
            await PollAsync(() => Task.FromResult(logs.Drops.Count == 1));
            Assert.Equal(1, effects.Effects);
            Assert.Equal(1, (await ReadAsync(handle)).Count);
            Assert.Equal(3, await handle.ExecuteUpdateAsync<int>("Add", [2]));
        }, effects: effects);
    }

    [Fact]
    public Task SignalAuthorization_AllowsConfiguredSignalAlongsideUpdateAuth() =>
        WithObjectAsync(async (handle, _, logs) =>
        {
            await handle.SignalAsync("Append", [8]);
            await PollAsync(async () => (await ReadAsync(handle)).Count == 8);
            Assert.Equal(10, await handle.ExecuteUpdateAsync<int>("Add", [2]));
            Assert.Empty(logs.Drops);
        }, new DurableObjectWorkerOptions { Authorize = _ => true, AuthorizeSignal = input => input.Signal == "Append" });

    [Fact]
    public Task DeactivationWinsOverAsyncSnapshot_NoRollover() =>
        WithObjectAsync(async (handle, barriers, _) =>
        {
            var firstRun = (await handle.DescribeAsync()).RunId;
            await handle.ExecuteUpdateAsync<object?>("RequestSnapshot", []);
            await barriers.SnapshotReached.Task.WaitAsync(Timeout);
            await handle.SignalAsync("BeginSignalDeactivation", []);
            await PollAsync(async () => await handle.QueryAsync<string>("ReadPhase", []) == "deactivating");
            barriers.ReleaseSnapshot.TrySetResult();
            await handle.GetResultAsync().WaitAsync(Timeout);
            Assert.Equal(firstRun, (await handle.DescribeAsync()).RunId);
            Assert.Equal(WorkflowExecutionStatus.Completed, (await handle.DescribeAsync()).Status);
        });

    [Fact]
    public Task RequestedWorkflowCancellation_PropagatesInsteadOfLoggingDrop() =>
        WithObjectAsync(async (handle, _, logs) =>
        {
            await handle.SignalAsync("AwaitCancellation", []);
            await PollAsync(async () => (await ReadAsync(handle)).Order.Contains("awaiting-cancel"));
            await handle.CancelAsync();
            await WaitForStatusAsync(handle, WorkflowExecutionStatus.Canceled, handle.Id);
            Assert.Empty(logs.Drops);
        });

    [Fact]
    public Task ContinueAsNewControlException_IsNotSwallowed() =>
        WithObjectAsync(async (handle, _, logs) =>
        {
            var firstRun = (await handle.DescribeAsync()).RunId;
            await handle.SignalAsync("Append", [6]);
            await PollAsync(async () => (await ReadAsync(handle)).Count == 6);
            await handle.SignalAsync("ContinueDirectly", []);
            await WaitForRunIdChangeAsync(handle, firstRun, handle.Id);
            Assert.Equal(6, (await ReadAsync(handle)).Count);
            Assert.Empty(logs.Drops);
        });

    [Fact]
    public Task UndecodableArguments_AreDroppedBeforeFrameworkAuthOrEvents() =>
        WithObjectAsync(async (handle, _, logs) =>
        {
            await handle.SignalAsync("Append", ["not-an-integer"]);
            await PollAsync(() => Task.FromResult(logs.Entries.Any(e =>
                e.Message.Contains("Failed decoding signal args", StringComparison.Ordinal))));
            Assert.Empty(logs.Drops);
            Assert.Equal(0, (await ReadAsync(handle)).Count);
            Assert.Equal(2, await handle.ExecuteUpdateAsync<int>("Add", [2]));
        }, new DurableObjectWorkerOptions
        {
            AuthorizeSignal = _ => throw new InvalidOperationException("Should never reach auth"),
        });

    [Fact]
    public Task ThrowingLogProvider_DoesNotDefeatHandlerContainment() =>
        WithObjectAsync(async (handle, _, logs) =>
        {
            await handle.SignalAsync("Fail", ["ordinary"]);
            await PollAsync(() => Task.FromResult(logs.Drops.Count == 1));
            Assert.Equal(3, await handle.ExecuteUpdateAsync<int>("Add", [2]));
            Assert.Equal(WorkflowExecutionStatus.Running, (await handle.DescribeAsync()).Status);
        }, throwOnDrop: true);

    [Fact]
    public Task SignalDuringSnapshot_DrainsSuspendedAndQueuedSignalsBeforeRollover() =>
        WithObjectAsync(async (handle, barriers, _) =>
        {
            var firstRun = (await handle.DescribeAsync()).RunId;
            await handle.ExecuteUpdateAsync<object?>("RequestSnapshot", []);
            await barriers.SnapshotReached.Task.WaitAsync(Timeout);
            await handle.SignalAsync("BlockSignal", [false]);
            await barriers.EnteredUpdateReached.Task.WaitAsync(Timeout);
            await handle.SignalAsync("Append", [7]);
            await WaitForSignalRecordedAsync(handle, "Append");
            barriers.ReleaseSnapshot.TrySetResult();
            // The snapshot Activity is finished, but signal user code is still suspended.
            Assert.Equal(firstRun, (await handle.DescribeAsync()).RunId);
            barriers.ReleaseEnteredUpdate.TrySetResult();
            await WaitForRunIdChangeAsync(handle, firstRun, handle.Id);
            Assert.Equal(17, (await ReadAsync(handle)).Count);
            Assert.Equal(2, (await ReadAsync(handle)).SnapshotPasses);
            Assert.Equal(new[] { "signal-start", "signal-end", "signal:7" }, (await ReadAsync(handle)).Order);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompiledInbox_RetainsFailedItem_RetriesAndDeduplicates_AcrossRollover(bool rollover)
    {
        var id = $"signal-inbox-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barrier = new Slice3Barrier();
        var activities = new RetryInboxActivities(barrier);
        using var worker = TestWorkerBuilder.Build(Client, tq,
            [typeof(RollingSignalInbox)], [activities]);
        using var cts = new CancellationTokenSource();
        var running = worker.ExecuteAsync(cts.Token);
        var handle = Client.GetWorkflowHandle(id);
        var first = new InboxEvent("event-1", "first");
        var second = new InboxEvent("event-2", "second");
        try
        {
            await Client.StartWorkflowAsync(nameof(RollingSignalInbox), [], new WorkflowOptions(id, tq)
            {
                StartSignal = "Enqueue", StartSignalArgs = [first],
            });
            var firstRun = (await handle.DescribeAsync()).RunId;
            await barrier.Reached.Task.WaitAsync(Timeout);
            // Timer awaits its Activity, but signal appends remain responsive and are not lost.
            await handle.SignalAsync("Enqueue", [first]);
            await handle.SignalAsync("Enqueue", [second]);
            await handle.SignalAsync("Enqueue", [new InboxEvent("", "invalid")]);
            await PollAsync(async () => (await handle.QueryAsync<InboxState>("ReadState", [])).Pending.Count == 2);
            if (rollover) await handle.ExecuteUpdateAsync<object?>("RequestRollover", []);
            barrier.Release.TrySetResult();
            if (rollover) await WaitForRunIdChangeAsync(handle, firstRun, id);
            await PollAsync(async () => (await handle.QueryAsync<InboxState>("ReadState", [])).FailedAttempts == 1);
            var failed = await handle.QueryAsync<InboxState>("ReadState", []);
            Assert.Equal(new[] { first, second }, failed.Pending);
            Assert.Empty(failed.RecentlyCompleted);
            await PollAsync(async () => (await handle.QueryAsync<InboxState>("ReadState", [])).Pending.Count == 0);
            var completed = await handle.QueryAsync<InboxState>("ReadState", []);
            Assert.Equal(new[] { "event-1", "event-2" }, completed.RecentlyCompleted);
            Assert.Equal(3, activities.Attempts);
            Assert.Equal(new[] { "event-1", "event-2" }, activities.SuccessfulIds);
            await handle.SignalAsync("Enqueue", [first]);
            await WaitForSignalRecordedAsync(handle, "Enqueue", rollover ? 1 : 5);
            // A subsequent update acknowledges a turn after the duplicate's handler.
            var fence = await handle.StartUpdateAsync("RequestRollover", [],
                new WorkflowUpdateStartOptions { WaitForStage = WorkflowUpdateStage.Completed });
            await fence.GetResultAsync();
            Assert.Empty((await handle.QueryAsync<InboxState>("ReadState", [])).Pending);
            Assert.Equal(3, activities.Attempts);
        }
        finally
        {
            barrier.Release.TrySetResult();
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(cts, running);
        }
    }

    private async Task WithObjectAsync(
        Func<WorkflowHandle, Slice3CanBarriers, SignalLogs, Task> test,
        DurableObjectWorkerOptions? options = null,
        bool throwOnDrop = false,
        SignalEffectActivities? effects = null)
    {
        var id = $"signals-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barriers = new Slice3CanBarriers();
        using var logs = new SignalLogs(throwOnDrop);
        using var loggerFactory = logs.Factory;
        using var worker = TestWorkerBuilder.Build(Client, tq,
            [typeof(SignalObject)], [new Slice3CanBarrierActivities(barriers), effects ?? new SignalEffectActivities()],
            options, logs.Factory);
        using var cts = new CancellationTokenSource();
        var running = worker.ExecuteAsync(cts.Token);
        var handle = Client.GetWorkflowHandle(id);
        try
        {
            await Client.StartWorkflowAsync(nameof(SignalObject), [], new WorkflowOptions(id, tq));
            await PollAsync(async () => await handle.QueryAsync<string>("ReadPhase", []) == "active");
            await test(handle, barriers, logs);
        }
        finally
        {
            barriers.ReleaseEnteredUpdate.TrySetResult();
            barriers.ReleaseSnapshot.TrySetResult();
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(cts, running);
        }
    }

    private static Task<SignalState> ReadAsync(WorkflowHandle handle) => handle.QueryAsync<SignalState>("ReadState", []);

    private static async Task WaitForSignalRecordedAsync(WorkflowHandle handle, string name, int count = 1)
    {
        await PollAsync(async () =>
        {
            var history = await handle.FetchHistoryAsync();
            return history.Events.Count(e => e.EventType == EventType.WorkflowExecutionSignaled &&
                e.WorkflowExecutionSignaledEventAttributes.SignalName == name) >= count;
        });
    }

    private static async Task PollAsync(Func<Task<bool>> condition)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < Timeout)
        {
            try
            {
                if (await condition()) return;
            }
            catch (WorkflowQueryFailedException) { }
            await Task.Delay(50);
        }
        throw new TimeoutException("Signal evidence condition was not observed within 20 seconds.");
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message,
        Exception? Exception, IReadOnlyDictionary<string, object?> Fields);

    private sealed class SignalLogs : ILoggerProvider
    {
        private readonly bool _throwOnDrop;
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public List<LogEntry> Drops => Entries.Where(e => e.EventId.Id == 4101).ToList();
        public ILoggerFactory Factory { get; }

        public SignalLogs(bool throwOnDrop)
        {
            _throwOnDrop = throwOnDrop;
            Factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(this));
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);
        public void Dispose() { } // Factory owner disposes separately; provider has no resources.

        private sealed class CapturingLogger(SignalLogs owner) : ILogger
        {
            public bool IsEnabled(LogLevel logLevel) => true;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.ToDictionary(p => p.Key, p => p.Value)
                    : new Dictionary<string, object?>();
                owner.Entries.Enqueue(new LogEntry(logLevel, eventId, formatter(state, exception), exception, fields));
                if (owner._throwOnDrop && eventId.Id == 4101)
                    throw new InvalidOperationException("Broken reporting sink.");
            }
        }
    }
}
