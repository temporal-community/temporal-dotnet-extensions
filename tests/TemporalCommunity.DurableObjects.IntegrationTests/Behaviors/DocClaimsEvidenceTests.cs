using System.Diagnostics;
using Temporalio.Api.Enums.V1;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Client;
using Temporalio.Exceptions;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;
using Xunit.Abstractions;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

/// <summary>
/// Current-behavior evidence for documentation claims R2a, R2b, R3 and R-limit.
/// These tests pin what the runtime does today; they are not statements of desired behavior.
/// </summary>
public sealed class DocClaimsEvidenceTests : DurableObjectTestBase
{
    private static readonly TimeSpan EvidenceTimeout = TimeSpan.FromSeconds(20);
    private readonly ITestOutputHelper _output;

    public DocClaimsEvidenceTests(WorkflowEnvironmentFixture fixture, ITestOutputHelper output)
        : base(fixture) => _output = output;

    // R2a (mode "none") and the "wrap only OnTimerAsync" half of R2b (mode "timer"):
    // the timer runs while the update is suspended and its write is lost.
    [Theory]
    [InlineData("none")]
    [InlineData("timer")]
    public async Task TimerRunsDuringSuspendedUpdate_AndItsWriteIsLost(string mode)
    {
        var id = $"docclaims-r2-{mode}-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barrier = new Slice3Barrier();
        using var worker = TestWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(TimerInterleavingObject)],
            activityInstances: [new Slice3BarrierActivities(barrier)]);
        using var workerCts = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(workerCts.Token);
        var handle = Client.GetWorkflowHandle(id);
        try
        {
            handle = await Client.StartWorkflowAsync(
                nameof(TimerInterleavingObject), [null], new WorkflowOptions(id, tq));

            var update = handle.ExecuteUpdateAsync<int>("ReadModifyWrite", [mode]);
            await barrier.Reached.Task.WaitAsync(EvidenceTimeout);

            // Timer fires and mutates State while the update is still parked at the barrier.
            await WaitForQueryAsync(handle, "ReadTimerPhase", "fired", id);
            var countDuringSuspension = await handle.QueryAsync<int>("ReadCount", []);
            Assert.False(update.IsCompleted);
            Assert.Equal(100, countDuringSuspension);

            barrier.Release.TrySetResult();
            var updateResult = await update.WaitAsync(EvidenceTimeout);
            var finalCount = await handle.QueryAsync<int>("ReadCount", []);
            _output.WriteLine(
                $"R2 mode={mode}: countDuringSuspension={countDuringSuspension}; " +
                $"updateResult={updateResult}; finalCount={finalCount}");

            Assert.Equal(1, updateResult);
            Assert.Equal(1, finalCount); // read(0)+1 overwrote the timer's +100: lost update.
        }
        finally
        {
            barrier.Release.TrySetResult();
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(workerCts, workerTask);
        }
    }

    // R2b: wrapping BOTH bodies in RunSerializedAsync makes the timer wait for the update.
    [Fact]
    public async Task TimerWaitsForUpdate_WhenBothBodiesUseRunSerializedAsync()
    {
        var id = $"docclaims-r2-both-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barrier = new Slice3Barrier();
        using var worker = TestWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(TimerInterleavingObject)],
            activityInstances: [new Slice3BarrierActivities(barrier)]);
        using var workerCts = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(workerCts.Token);
        var handle = Client.GetWorkflowHandle(id);
        try
        {
            handle = await Client.StartWorkflowAsync(
                nameof(TimerInterleavingObject), [null], new WorkflowOptions(id, tq));
            var runId = (await handle.DescribeAsync()).RunId;

            var update = handle.ExecuteUpdateAsync<int>("ReadModifyWrite", ["both"]);
            await barrier.Reached.Task.WaitAsync(EvidenceTimeout);

            // Deterministic: wait until the run-loop timer has fired AND a workflow task has
            // completed after it, so OnTimerAsync has definitely been invoked.
            await WaitForTimerFiredAndProcessedAsync(handle.Id, runId);
            var phaseDuringSuspension = await handle.QueryAsync<string>("ReadTimerPhase", []);
            var countDuringSuspension = await handle.QueryAsync<int>("ReadCount", []);
            Assert.False(update.IsCompleted);
            Assert.Equal("entered", phaseDuringSuspension); // entered OnTimerAsync, blocked at gate
            Assert.Equal(0, countDuringSuspension);

            barrier.Release.TrySetResult();
            var updateResult = await update.WaitAsync(EvidenceTimeout);
            await WaitForQueryAsync(handle, "ReadTimerPhase", "fired", id);
            var finalCount = await handle.QueryAsync<int>("ReadCount", []);
            _output.WriteLine(
                $"R2 mode=both: phaseDuringSuspension={phaseDuringSuspension}; " +
                $"countDuringSuspension={countDuringSuspension}; updateResult={updateResult}; " +
                $"finalCount={finalCount}");

            Assert.Equal(1, updateResult);
            Assert.Equal(101, finalCount);
        }
        finally
        {
            barrier.Release.TrySetResult();
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(workerCts, workerTask);
        }
    }

    // R3: OnTimerAsync throws a plain exception while one update is in-flight (suspended in the
    // handler) and another is queued behind the interceptor gate.
    [Fact]
    public async Task OnTimerAsyncPlainException_FailsRun_AndNextClientCallStartsFreshRun()
    {
        var id = $"docclaims-r3-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barrier = new DocClaimsTwoPhaseBarrier();
        using var worker = TestWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(FailingTimerObject)],
            activityInstances: [new DocClaimsTwoPhaseBarrierActivities(barrier)]);
        using var workerCts = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(workerCts.Token);
        var handle = Client.GetWorkflowHandle(id);
        var callOptions = new DurableObjectCallOptions(rpcTimeout: EvidenceTimeout, retry: false);
        try
        {
            var client = TestFactory.Create(Client, tq).GetFailingTimerObjectClient(id);
            Assert.Equal(5, await client.AddAsync(5, callOptions));
            var firstRunId = (await handle.DescribeAsync()).RunId;

            var inFlight = client.AddThenArmFailingTimerAndBlockAsync(1, callOptions);
            await barrier.FirstReached.Task.WaitAsync(EvidenceTimeout);
            var queued = client.AddAsync(10, callOptions);
            // Both updates accepted (first add + in-flight + queued = 3) before arming the timer.
            await WaitForHistoryCountAsync(id, firstRunId, EventType.WorkflowExecutionUpdateAccepted, 3);

            // Releasing phase 1 lets the in-flight handler arm an immediately-due timer and then
            // suspend at its next await; the run loop fires OnTimerAsync in the same activation.
            barrier.ReleaseFirst.TrySetResult();

            await WaitForStatusAsync(Client.GetWorkflowHandle(id, firstRunId), WorkflowExecutionStatus.Failed, id);
            var runFailure = await Assert.ThrowsAsync<WorkflowFailedException>(
                () => Client.GetWorkflowHandle(id, firstRunId).GetResultAsync());
            var app = Assert.IsType<ApplicationFailureException>(runFailure.InnerException);
            _output.WriteLine(
                $"R3 run: status=Failed; outer={runFailure.GetType().Name}: {runFailure.Message}; " +
                $"inner={app.GetType().Name}; errorType={app.ErrorType}; nonRetryable={app.NonRetryable}; " +
                $"message={app.Message}; cause={app.InnerException?.GetType().Name}: {app.InnerException?.Message} " +
                $"(causeErrorType={(app.InnerException as ApplicationFailureException)?.ErrorType})");

            var inFlightFailure = await CaptureFailureAsync(inFlight);
            var queuedFailure = await CaptureFailureAsync(queued);
            _output.WriteLine($"R3 in-flight caller: {Describe(inFlightFailure)}");
            _output.WriteLine($"R3 queued caller: {Describe(queuedFailure)}");

            var history = await ReadHistoryAsync(id, firstRunId);
            _output.WriteLine($"R3 failed-run history: accepted={history[EventType.WorkflowExecutionUpdateAccepted]}; " +
                $"completed={history[EventType.WorkflowExecutionUpdateCompleted]}; " +
                $"activitiesScheduled={history.GetValueOrDefault(EventType.ActivityTaskScheduled)}; " +
                $"secondPhaseActivityStarted={barrier.SecondReached.Task.IsCompleted}");

            var nextResult = await client.AddAsync(1, callOptions);
            var nextRun = await handle.DescribeAsync();
            var nextCount = await client.ReadCountAsync(callOptions);
            _output.WriteLine(
                $"R3 next generated-client AddAsync(1): result={nextResult}; firstRun={firstRunId}; " +
                $"newRun={nextRun.RunId}; status={nextRun.Status}; queriedCount={nextCount}");

            Assert.Equal("TimerFailure", app.ErrorType);
            Assert.True(app.NonRetryable);
            Assert.Equal("DurableObject timer 'boom' failed: plain failure from timer 'boom'", app.Message);
            foreach (var callerFailure in new[] { inFlightFailure, queuedFailure })
            {
                var updateFailure = Assert.IsType<WorkflowUpdateFailedException>(callerFailure);
                var cause = Assert.IsType<ApplicationFailureException>(updateFailure.InnerException);
                Assert.Equal("AcceptedUpdateCompletedWorkflow", cause.ErrorType);
            }

            Assert.Equal(1, history[EventType.WorkflowExecutionUpdateCompleted]); // only the first AddAsync(5)
            Assert.NotEqual(firstRunId, nextRun.RunId);
            Assert.Equal(1, nextResult); // state 5 (and the in-flight +1) is not carried.
            Assert.Equal(1, nextCount);
            barrier.ReleaseSecond.TrySetResult();
            await handle.ExecuteUpdateAsync<object?>("Deactivate", []);
        }
        finally
        {
            barrier.ReleaseFirst.TrySetResult();
            barrier.ReleaseSecond.TrySetResult();
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(workerCts, workerTask);
        }
    }

    // R-limit: one update blocked inside the handler (holding the interceptor gate), then 12
    // concurrent generated-client updates to the same object.
    [Fact]
    public async Task InFlightUpdateLimit_WithOneBlockedUpdate_RejectsOverflow()
    {
        const int concurrent = 12;
        var id = $"docclaims-limit-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barrier = new Slice3Barrier();
        using var worker = TestWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(InFlightLimitObject)],
            activityInstances: [new Slice3BarrierActivities(barrier)]);
        using var workerCts = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(workerCts.Token);
        var handle = Client.GetWorkflowHandle(id);
        var callOptions = new DurableObjectCallOptions(rpcTimeout: EvidenceTimeout, retry: false);
        try
        {
            var info = await Client.Connection.WorkflowService.GetSystemInfoAsync(new());
            _output.WriteLine(
                $"R-limit server: version={info.ServerVersion}; " +
                $"SDK={typeof(TemporalClient).Assembly.GetName().Version}; " +
                "fixture=WorkflowEnvironment.StartLocalAsync() with no dynamic-config overrides");

            var client = TestFactory.Create(Client, tq).GetInFlightLimitObjectClient(id);
            Assert.Equal(1, await client.IncrementAsync(callOptions));
            var runId = (await handle.DescribeAsync()).RunId;

            var hold = client.HoldAsync(callOptions);
            await barrier.Reached.Task.WaitAsync(EvidenceTimeout);

            var calls = Enumerable.Range(0, concurrent)
                .Select(_ => client.IncrementAsync(callOptions))
                .ToArray();

            // Bounded wait: every call is either rejected or accepted into history.
            var timer = Stopwatch.StartNew();
            int faulted = 0, acceptedIncrements = 0;
            while (timer.Elapsed < EvidenceTimeout)
            {
                faulted = calls.Count(c => c.IsFaulted);
                var accepted = (await ReadHistoryAsync(id, runId))[EventType.WorkflowExecutionUpdateAccepted];
                acceptedIncrements = accepted - 2; // minus initial Increment and Hold
                if (faulted + acceptedIncrements == concurrent)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            Assert.Equal(concurrent, faulted + acceptedIncrements);
            Assert.All(calls.Where(c => !c.IsFaulted), c => Assert.False(c.IsCompleted));
            var failures = calls.Where(c => c.IsFaulted).Select(c => c.Exception!.InnerException!).ToList();
            foreach (var failure in failures)
            {
                _output.WriteLine($"R-limit rejected caller: {Describe(failure)}");
            }

            _output.WriteLine(
                $"R-limit: concurrent={concurrent}; admittedWhileBlocked={acceptedIncrements}; " +
                $"rejected={faulted}; inFlightIncludingHold={acceptedIncrements + 1}");
            Assert.Equal(9, acceptedIncrements); // 1 held + 9 = server in-flight limit of 10
            Assert.Equal(3, faulted);
            Assert.All(failures, failure =>
            {
                var rpc = Assert.IsType<RpcException>(failure);
                Assert.Equal(RpcException.StatusCode.ResourceExhausted, rpc.Code);
                Assert.Equal(
                    "limit on number of concurrent in-flight updates has been reached (10)", rpc.Message);
            });

            barrier.Release.TrySetResult();
            Assert.Equal(1, await hold.WaitAsync(EvidenceTimeout));
            var admittedResults = await Task.WhenAll(
                calls.Where(c => !c.IsFaulted).Select(c => c.WaitAsync(EvidenceTimeout)));
            var after = await client.IncrementAsync(callOptions);
            var count = await client.ReadCountAsync(callOptions);
            var finalRunId = (await handle.DescribeAsync()).RunId;
            _output.WriteLine(
                $"R-limit after release: admittedResults=[{string.Join(",", admittedResults.Order())}]; " +
                $"nextIncrement={after}; count={count}; sameRun={finalRunId == runId}");

            Assert.Equal(1 + acceptedIncrements + 1, after);
            Assert.Equal(after, count);
            Assert.Equal(runId, finalRunId);
            await handle.ExecuteUpdateAsync<object?>("Deactivate", []);
        }
        finally
        {
            barrier.Release.TrySetResult();
            await TerminateIfRunningAsync(handle, id);
            await StopWorkerAsync(workerCts, workerTask);
        }
    }

    private static async Task<Exception?> CaptureFailureAsync(Task task)
    {
        try
        {
            await task.WaitAsync(EvidenceTimeout);
            return null;
        }
        catch (TimeoutException ex)
        {
            return ex;
        }
#pragma warning disable CA1031 // Evidence capture: report whatever the caller sees.
        catch (Exception ex)
        {
            return ex;
        }
#pragma warning restore CA1031
    }

    private static string Describe(Exception? ex)
    {
        if (ex is null)
        {
            return "no exception (completed successfully)";
        }

        var parts = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var detail = current switch
            {
                RpcException rpc => $" code={rpc.Code}",
                ApplicationFailureException app => $" errorType={app.ErrorType} nonRetryable={app.NonRetryable}",
                _ => string.Empty,
            };
            parts.Add($"{current.GetType().FullName}{detail} message=\"{current.Message}\"");
        }

        return string.Join(" -> ", parts);
    }

    private async Task WaitForQueryAsync(WorkflowHandle handle, string query, string expected, string id)
    {
        var timer = Stopwatch.StartNew();
        string? last = null;
        while (timer.Elapsed < EvidenceTimeout)
        {
            last = await handle.QueryAsync<string>(query, []);
            if (last == expected)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException($"[{id}] Query '{query}' last='{last}', expected '{expected}'.");
    }

    private async Task WaitForTimerFiredAndProcessedAsync(string id, string runId)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < EvidenceTimeout)
        {
            var events = await ReadEventsAsync(id, runId);
            var fired = events.FindIndex(e => e == EventType.TimerFired);
            if (fired >= 0 && events.Skip(fired).Contains(EventType.WorkflowTaskCompleted))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException($"[{id}] TimerFired + WorkflowTaskCompleted not observed.");
    }

    private async Task WaitForHistoryCountAsync(string id, string runId, EventType type, int expected)
    {
        var timer = Stopwatch.StartNew();
        var last = 0;
        while (timer.Elapsed < EvidenceTimeout)
        {
            last = (await ReadHistoryAsync(id, runId))[type];
            if (last >= expected)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException($"[{id}] {type} count {last}, expected {expected}.");
    }

    private async Task<Dictionary<EventType, int>> ReadHistoryAsync(string id, string runId)
    {
        var counts = new Dictionary<EventType, int>
        {
            [EventType.WorkflowExecutionUpdateAccepted] = 0,
            [EventType.WorkflowExecutionUpdateCompleted] = 0,
        };
        foreach (var type in await ReadEventsAsync(id, runId))
        {
            counts[type] = counts.GetValueOrDefault(type) + 1;
        }

        return counts;
    }

    private async Task<List<EventType>> ReadEventsAsync(string id, string runId)
    {
        var request = new GetWorkflowExecutionHistoryRequest
        {
            Namespace = Client.Options.Namespace,
            Execution = new Temporalio.Api.Common.V1.WorkflowExecution { WorkflowId = id, RunId = runId },
        };
        var events = new List<EventType>();
        do
        {
            var page = await Client.Connection.WorkflowService.GetWorkflowExecutionHistoryAsync(request);
            events.AddRange(page.History.Events.Select(e => e.EventType));
            request.NextPageToken = page.NextPageToken;
        }
        while (!request.NextPageToken.IsEmpty);
        return events;
    }

    private static async Task WaitForStatusAsync(
        WorkflowHandle handle, WorkflowExecutionStatus expected, string id)
    {
        var timer = Stopwatch.StartNew();
        WorkflowExecutionStatus last = default;
        while (timer.Elapsed < EvidenceTimeout)
        {
            last = (await handle.DescribeAsync()).Status;
            if (last == expected)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException($"[{id}] Expected {expected}, last {last}.");
    }

    private static async Task TerminateIfRunningAsync(WorkflowHandle handle, string id)
    {
        try
        {
            var current = handle.Client.GetWorkflowHandle(id);
            if ((await current.DescribeAsync()).Status == WorkflowExecutionStatus.Running)
            {
                await current.TerminateAsync($"DocClaims evidence cleanup: {id}");
            }
        }
        catch (RpcException)
        {
            // Never started or closed between describe/terminate.
        }
    }

    private static async Task StopWorkerAsync(CancellationTokenSource workerCts, Task workerTask)
    {
        await workerCts.CancelAsync();
        try
        {
            await workerTask.WaitAsync(EvidenceTimeout);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
