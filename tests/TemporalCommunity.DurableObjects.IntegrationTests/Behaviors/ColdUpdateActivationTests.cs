using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Common;
using Temporalio.Worker;
using Temporalio.Worker.Interceptors;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;
using Xunit.Abstractions;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

/// <summary>
/// Cold update activation admission and real server history replay across restart and rollover.
/// </summary>
public sealed class ColdUpdateActivationTests : DurableObjectTestBase
{
    private static readonly TimeSpan EvidenceTimeout = TimeSpan.FromSeconds(20);
    private static readonly string[] ActivityOrder = ["Wait", "ColdUpdateProbe"];
    private readonly ITestOutputHelper _output;

    public ColdUpdateActivationTests(WorkflowEnvironmentFixture fixture, ITestOutputHelper output)
        : base(fixture) => _output = output;

    [Fact]
    public async Task ColdUpdateWithStart_FirstUpdateWaitsForActivation()
    {
        var id = $"cold-uws-activation-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barrier = new Slice3Barrier();
        using var worker = TestWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(ColdUpdateActivationObject)],
            activityInstances: [new Slice3BarrierActivities(barrier), new ColdUpdateProbeActivities()]);
        using var workerCts = new CancellationTokenSource();
        var workerTask = worker.ExecuteAsync(workerCts.Token);
        var handle = Client.GetWorkflowHandle(id);

        try
        {
            // Cold object: nothing started yet. The generated client issues update-with-start.
            var client = TestFactory.Create(Client, tq).GetColdUpdateActivationObjectClient(id);
            var firstUpdate = client.ObserveUpdateAsync();

            // OnActivateAsync is now blocked in the barrier activity.
            await barrier.Reached.Task.WaitAsync(EvidenceTimeout);

            // Give a buggy handler ample time to complete while activation is still pending.
            var winner = await Task.WhenAny(firstUpdate, Task.Delay(TimeSpan.FromSeconds(2)));
            var completedBeforeRelease = winner == firstUpdate;
            _output.WriteLine($"completedBeforeActivationRelease={completedBeforeRelease}");
            if (completedBeforeRelease)
            {
                _output.WriteLine($"earlyResult={await firstUpdate}");
            }

            barrier.Release.TrySetResult();
            var observed = await firstUpdate.WaitAsync(EvidenceTimeout);
            _output.WriteLine($"observed={observed}");

            var events = new List<string>();
            await foreach (var e in handle.FetchHistoryEventsAsync())
            {
                events.Add(e.EventType.ToString());
            }

            _output.WriteLine("history=" + string.Join(", ", events));

            Assert.False(completedBeforeRelease, "First cold update completed while OnActivateAsync was still blocked.");
            Assert.Equal("update:active", observed);
        }
        finally
        {
            barrier.Release.TrySetResult();
            try
            {
                if ((await handle.DescribeAsync()).Status == WorkflowExecutionStatus.Running)
                {
                    await handle.TerminateAsync("cold update activation test cleanup");
                }
            }
            catch (RpcException)
            {
            }

            await workerCts.CancelAsync();
            try { await workerTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task ColdHistory_ReplaysAndRestarts_ThenRolloverReplays()
    {
        var id = $"cold-replay-{Guid.NewGuid():N}";
        var tq = UniqueTaskQueue();
        var barrier = new Slice3Barrier();
        var handle = Client.GetWorkflowHandle(id);
        var recordOptions = Options(tq, barrier, new DurableObjectWorkerInterceptor());
        using var recordWorker = new TemporalWorker(Client, recordOptions);
        using var recordCts = new CancellationTokenSource();
        var recordTask = recordWorker.ExecuteAsync(recordCts.Token);
        string firstRun;

        try
        {
            var client = TestFactory.Create(Client, tq).GetColdUpdateActivationObjectClient(id);
            var firstUpdate = client.ObserveUpdateAsync();
            await barrier.Reached.Task.WaitAsync(EvidenceTimeout);
            Assert.False(firstUpdate.IsCompleted);

            barrier.Release.TrySetResult();
            Assert.Equal("update:active", await firstUpdate.WaitAsync(EvidenceTimeout));
            await WaitForActiveAsync(handle);
            firstRun = (await handle.DescribeAsync()).RunId;
            var history = await handle.FetchHistoryAsync();
            AssertAdmissionHistory(history);
            await ReplayAsync(history);

            // Preserve the fetched, unedited, real server history as a replay artifact.
            var artifact = Path.Combine(AppContext.BaseDirectory, "TestData",
                "cold-update-recorded.json");
            Directory.CreateDirectory(Path.GetDirectoryName(artifact)!);
            await File.WriteAllTextAsync(artifact, history.ToJson());
            _output.WriteLine("recordedHistory=" + artifact);
        }
        finally
        {
            barrier.Release.TrySetResult();
            await recordCts.CancelAsync();
            try { await recordTask; } catch (OperationCanceledException) { }
        }

        // A different worker has an empty cache and MUST replay the recorded prefix before
        // handling another update.
        using var fixedWorker = new TemporalWorker(Client,
            Options(tq, barrier, new DurableObjectWorkerInterceptor()));
        using var fixedCts = new CancellationTokenSource();
        var fixedTask = fixedWorker.ExecuteAsync(fixedCts.Token);
        try
        {
            Assert.Equal("update:active",
                await handle.ExecuteUpdateAsync<string>("ObserveUpdate", [])
                    .WaitAsync(EvidenceTimeout));
            await handle.ExecuteUpdateAsync("RequestRollover", []).WaitAsync(EvidenceTimeout);
            await WaitForNextRunAsync(handle, firstRun);
            var closedHistory = await Client.GetWorkflowHandle(id, firstRun).FetchHistoryAsync();
            Assert.DoesNotContain(closedHistory.Events, e => e.EventType == EventType.WorkflowTaskFailed);
            Assert.Contains(closedHistory.Events,
                e => e.EventType == EventType.WorkflowExecutionContinuedAsNew);
            await ReplayAsync(closedHistory);
            Assert.Equal("update:active",
                await handle.ExecuteUpdateAsync<string>("ObserveUpdate", [])
                    .WaitAsync(EvidenceTimeout));
            var nextHistory = await handle.FetchHistoryAsync();
            Assert.DoesNotContain(nextHistory.Events, e => e.EventType == EventType.WorkflowTaskFailed);
            await ReplayAsync(nextHistory);
            _output.WriteLine("restart update=active; prior run replay=OK; next run replay=OK");
        }
        finally
        {
            await handle.TerminateAsync("cold replay cleanup");
            await fixedCts.CancelAsync();
            try { await fixedTask; } catch (OperationCanceledException) { }
        }
    }

    private static TemporalWorkerOptions Options(
        string tq, Slice3Barrier barrier, IWorkerInterceptor interceptor) =>
        new TemporalWorkerOptions(tq)
        {
            Interceptors = [interceptor],
        }
        .AddWorkflow<ColdUpdateActivationObject>()
        .AddAllActivities(new Slice3BarrierActivities(barrier))
        .AddAllActivities(new ColdUpdateProbeActivities());

    private void AssertAdmissionHistory(WorkflowHistory history)
    {
        var scheduled = history.Events
            .Where(e => e.EventType == EventType.ActivityTaskScheduled)
            .Select(e => e.ActivityTaskScheduledEventAttributes.ActivityType.Name)
            .ToArray();
        _output.WriteLine("ActivityTaskScheduled=" + string.Join(", ", scheduled));
        Assert.Equal(ActivityOrder, scheduled);
        Assert.DoesNotContain(history.Events, e => e.EventType == EventType.WorkflowTaskFailed);
    }

    private static async Task ReplayAsync(WorkflowHistory history)
    {
        var options = new WorkflowReplayerOptions
        {
            Interceptors = [new DurableObjectWorkerInterceptor()],
        };
        options.AddWorkflow<ColdUpdateActivationObject>();
        var replay = await new WorkflowReplayer(options).ReplayWorkflowAsync(history);
        Assert.Null(replay.ReplayFailure);
    }

    private static async Task WaitForActiveAsync(WorkflowHandle handle)
    {
        using var timeout = new CancellationTokenSource(EvidenceTimeout);
        while (true)
        {
            try
            {
                if (await handle.QueryAsync<string>("ReadPhase", []) == "active")
                {
                    return;
                }
            }
            catch (WorkflowQueryFailedException)
            {
            }

            await Task.Delay(50, timeout.Token);
        }
    }

    private static async Task WaitForNextRunAsync(WorkflowHandle handle, string oldRun)
    {
        using var timeout = new CancellationTokenSource(EvidenceTimeout);
        while ((await handle.DescribeAsync()).RunId == oldRun)
        {
            await Task.Delay(50, timeout.Token);
        }

        await WaitForActiveAsync(handle);
    }
}
