using System.Diagnostics;
using Temporalio.Api.Enums.V1;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Testing;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;
using Xunit.Abstractions;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Behaviors;

public sealed class DefaultUpdateLimitEvidenceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DefaultOptions_SequentialUpdates_RollOverBeforeTotalUpdateLimit()
    {
        const int updates = 2_200;
        const int limit = 2_000;
        var started = DateTimeOffset.UtcNow;
        var duration = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        // Dedicated in-memory server, random port; never change the collection's shared server.
        var env = await WorkflowEnvironment.StartLocalAsync(new()
        {
            DevServerOptions = new()
            {
                ExtraArgs =
                [
                    "--dynamic-config-value", "history.maxTotalUpdates=2000",
                    "--dynamic-config-value", "history.maxTotalUpdates.suggestContinueAsNewThreshold=0.9",
                ],
            },
        });
        var id = $"default-update-limit-{Guid.NewGuid():N}";
        var tq = $"tq-{Guid.NewGuid():N}";
        var handle = env.Client.GetWorkflowHandle(id);
        var control = env.Client.GetWorkflowHandle($"{id}-server-control");
        var rpc = new RpcOptions
        {
            Timeout = TimeSpan.FromSeconds(20),
            CancellationToken = deadline.Token,
            Retry = false,
        };
        var successful = 0;
        var rejections = 0;
        var runIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var info = await env.Client.Connection.WorkflowService.GetSystemInfoAsync(new(), rpc);
            output.WriteLine(
                $"START {started:O}; SDK={typeof(WorkflowEnvironment).Assembly.GetName().Version}; " +
                $"server={info.ServerVersion}; object={id}; maxHistory={new DurableObjectOptions().MaxHistoryLength}; " +
                $"history.maxTotalUpdates={limit}; suggestionThreshold=0.9");
            Assert.Equal(10_000, new DurableObjectOptions().MaxHistoryLength);
            using var worker = TestWorkerBuilder.Build(
                env.Client, tq,
                workflowTypes: [typeof(DefaultUpdateLimitCounter), typeof(UpdateLimitServerControl)]);
            using var workerCts = new CancellationTokenSource();
            var workerTask = worker.ExecuteAsync(workerCts.Token);
            try
            {
                var factory = TestFactory.Create(env.Client, tq);
                var counter = factory.GetDefaultUpdateLimitCounterClient(id);
                Assert.IsType<DefaultUpdateLimitCounterDurableObjectClient>(counter);
                var callOptions = new DurableObjectCallOptions(
                    rpcTimeout: TimeSpan.FromSeconds(20), retry: false,
                    cancellationToken: deadline.Token);
                while (successful < updates)
                {
                    var retries = 0;
                    while (true)
                    {
                        try
                        {
                            Assert.Equal(successful + 1, await counter.IncrementAsync(callOptions));
                            successful++;
                            break;
                        }
                        catch (WorkflowUpdateFailedException ex) when (
                            ex.InnerException is ApplicationFailureException
                            {
                                ErrorType: "ObjectContinuingAsNew",
                                NonRetryable: true,
                            })
                        {
                            output.WriteLine($"PRE-HANDLER REJECTION at successful={successful}: {ex}");
                            rejections++;
                            Assert.True(++retries <= 20, "Continue-as-New admission did not recover.");
                            await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
                        }
                    }

                    // Sample every successful mutation; retain IDs even across fast rollover.
                    var description = await handle.DescribeAsync(new() { Rpc = rpc });
                    runIds.Add(description.RunId);
                }

                var state = await counter.ReadStateAsync(callOptions);
                Assert.Equal(successful, state.Count);
                Assert.Equal(updates, successful);
                Assert.NotEmpty(state.Rollovers);
                foreach (var rollover in state.Rollovers)
                {
                    runIds.Add(rollover.RunId);
                    output.WriteLine($"ROLLOVER {rollover}");
                    Assert.True(rollover.Suggested || rollover.HistoryLength >= 10_000);
                }

                var totalCompleted = 0;
                foreach (var runId in runIds)
                {
                    var history = await ReadHistoryAsync(env.Client, id, runId, rpc);
                    output.WriteLine($"HISTORY run={runId}; events={history.Events}; " +
                        $"accepted={history.Accepted}; completed={history.Completed}; CAN={history.Continued}");
                    Assert.True(history.Completed < limit,
                        $"Run {runId} reached the configured total Update limit.");
                    Assert.Equal(history.Accepted, history.Completed);
                    Assert.Equal(state.Rollovers.Any(rollover => rollover.RunId == runId), history.Continued);
                    totalCompleted += history.Completed;
                }

                Assert.Equal(successful, totalCompleted);
                Assert.Equal(state.Rollovers.Count + 1, runIds.Count);
                output.WriteLine($"RESULT successful={successful}; state={state.Count}; " +
                    $"rollovers={state.Rollovers.Count}; preHandlerRejections={rejections}; errors=none");

                control = await env.Client.StartWorkflowAsync(
                    nameof(UpdateLimitServerControl), [],
                    new WorkflowOptions(control.Id, tq) { Rpc = rpc });
                for (var i = 1; i <= limit; i++)
                {
                    Assert.Equal(i, await control.ExecuteUpdateAsync<int>(
                        "Increment", [], new() { Rpc = rpc }));
                }

                // A known server-limit rejection is inspected, not retried.
                var limitFailure = await Assert.ThrowsAsync<RpcException>(
                    () => control.ExecuteUpdateAsync<int>("Increment", [], new() { Rpc = rpc }));
                Assert.Equal(RpcException.StatusCode.FailedPrecondition, limitFailure.Code);
                Assert.Contains(
                    "limit on the total number of distinct updates in this workflow has been reached (2000)",
                    limitFailure.Message, StringComparison.Ordinal);
                Assert.Equal(limit, await control.QueryAsync<int>("ReadCount", [], new() { Rpc = rpc }));
                var controlRun = await control.DescribeAsync(new() { Rpc = rpc });
                var controlHistory = await ReadHistoryAsync(env.Client, control.Id, controlRun.RunId, rpc);
                Assert.Equal(limit, controlHistory.Completed);
                Assert.False(controlHistory.Continued);
                output.WriteLine($"SERVER LIMIT CONTROL successful={limit}; state={limit}; " +
                    $"run={controlRun.RunId}; events={controlHistory.Events}; " +
                    $"accepted={controlHistory.Accepted}; completed={controlHistory.Completed}; " +
                    $"CAN={controlHistory.Continued}; attempt={limit + 1}; " +
                    $"error={limitFailure.GetType().Name}; code={limitFailure.Code}; message={limitFailure.Message}");
            }
            catch (Exception ex)
            {
                output.WriteLine($"FAILURE successful={successful}; preHandlerRejections={rejections}; {ex}");
                var description = await handle.DescribeAsync(new()
                {
                    Rpc = new() { Timeout = TimeSpan.FromSeconds(10), Retry = false },
                });
                var history = await ReadHistoryAsync(env.Client, id, description.RunId,
                    new() { Timeout = TimeSpan.FromSeconds(10), Retry = false });
                output.WriteLine($"FAILURE HISTORY run={description.RunId}; status={description.Status}; " +
                    $"events={history.Events}; accepted={history.Accepted}; completed={history.Completed}");
                throw; // Unknown outcomes/limit errors are evidence, never retry mutations.
            }
            finally
            {
                try
                {
                    await handle.TerminateAsync("Default Update limit evidence cleanup", options: new()
                    {
                        Rpc = new() { Timeout = TimeSpan.FromSeconds(10), Retry = false },
                    });
                }
                catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound) { }
                finally
                {
                    try
                    {
                        await control.TerminateAsync("Server limit control cleanup", options: new()
                        {
                            Rpc = new() { Timeout = TimeSpan.FromSeconds(10), Retry = false },
                        });
                    }
                    catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound) { }
                    finally
                    {
                        await workerCts.CancelAsync();
                        try { await workerTask.WaitAsync(TimeSpan.FromSeconds(20)); }
                        catch (OperationCanceledException) { }
                    }
                }
            }
        }
        finally
        {
            await env.ShutdownAsync();
            output.WriteLine($"END {DateTimeOffset.UtcNow:O}; duration={duration.Elapsed}; successful={successful}");
        }
    }

    private static async Task<(int Events, int Accepted, int Completed, bool Continued)> ReadHistoryAsync(
        ITemporalClient client, string id, string runId, RpcOptions rpc)
    {
        var request = new GetWorkflowExecutionHistoryRequest
        {
            Namespace = client.Options.Namespace,
            Execution = new Temporalio.Api.Common.V1.WorkflowExecution { WorkflowId = id, RunId = runId },
        };
        var events = 0;
        var accepted = 0;
        var completed = 0;
        var continued = false;
        do
        {
            var page = await client.Connection.WorkflowService.GetWorkflowExecutionHistoryAsync(request, rpc);
            foreach (var entry in page.History.Events)
            {
                events++;
                if (entry.EventType == EventType.WorkflowExecutionUpdateAccepted) accepted++;
                if (entry.EventType == EventType.WorkflowExecutionUpdateCompleted) completed++;
                if (entry.EventType == EventType.WorkflowExecutionContinuedAsNew) continued = true;
            }
            request.NextPageToken = page.NextPageToken;
        }
        while (!request.NextPageToken.IsEmpty);
        return (events, accepted, completed, continued);
    }
}
