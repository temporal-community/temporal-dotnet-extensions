using Temporalio.Common;
using Temporalio.Worker;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests;

public sealed class ReplayHistoryTests
{
    [Fact]
    public async Task TypedStateHistory_ReplaysWithCurrentImplementation()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "rolling-counter.json");
        var history = WorkflowHistory.FromJson(
            "rolling-counter-replay",
            await File.ReadAllTextAsync(fixturePath));
        var options = new WorkflowReplayerOptions
        {
            Interceptors = [new DurableObjectWorkerInterceptor()],
        };
        options.AddWorkflow<RollingCounter>();

        var result = await new WorkflowReplayer(options).ReplayWorkflowAsync(history);

        Assert.Null(result.ReplayFailure);
    }
}
