using FakeItEasy;
using Temporalio.Client;
using Xunit;

namespace TemporalCommunity.DurableObjects.GeneratedClients.Tests;

public sealed class GeneratedClientExecutionTests
{
    [Fact]
    public async Task EmittedClientExecutesUpdateWithCallOptions()
    {
        var temporalClient = A.Fake<ITemporalClient>();
        WorkflowStartUpdateWithStartOptions? captured = null;
        A.CallTo(() => temporalClient.StartUpdateWithStartWorkflowAsync<int>(
                A<string>._,
                A<IReadOnlyCollection<object?>>._,
                A<WorkflowStartUpdateWithStartOptions>._))
            .Invokes(call => captured = call.GetArgument<WorkflowStartUpdateWithStartOptions>(2))
            .ThrowsAsync(new InvalidOperationException("captured"));
        var factory = new DurableObjectFactory(temporalClient, "generated-queue");
        var options = new DurableObjectCallOptions(
            rpcTimeout: TimeSpan.FromSeconds(4),
            metadata: new Dictionary<string, string> { ["authorization"] = "Bearer emitted" });

        var generated = factory.GetGeneratedCounterClient("counter-id");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => generated.AddAsync(2, options));

        Assert.IsType<GeneratedCounterDurableObjectClient>(generated);
        Assert.Equal("captured", exception.Message);
        Assert.Equal(TimeSpan.FromSeconds(4), captured!.Rpc!.Timeout);
        Assert.Equal(
            "Bearer emitted",
            captured.Rpc.Metadata!.Single(pair => pair.Key == "authorization").Value);
    }

    [Fact]
    public async Task EmittedClientExecutesAsynchronousQuery()
    {
        var temporalClient = A.Fake<ITemporalClient>();
        var handle = new CapturingWorkflowHandle(temporalClient);
        A.CallTo(() => temporalClient.GetWorkflowHandle(A<string>._, A<string?>._, A<string?>._))
            .Returns(handle);
        var factory = new DurableObjectFactory(temporalClient, "generated-queue");
        var options = new DurableObjectCallOptions(rpcTimeout: TimeSpan.FromSeconds(6));

        var generated = factory.GetGeneratedCounterClient("counter-id");
        var result = await generated.GetValueAsync(options);

        Assert.Equal(42, result);
        Assert.Equal("read-value", handle.QueryName);
        Assert.Equal(TimeSpan.FromSeconds(6), handle.Options!.Rpc!.Timeout);
    }

    private sealed record CapturingWorkflowHandle(ITemporalClient Client)
        : WorkflowHandle(Client, "counter-id")
    {
        internal string? QueryName { get; private set; }

        internal WorkflowQueryOptions? Options { get; private set; }

        public override Task<TResult> QueryAsync<TResult>(
            string query,
            IReadOnlyCollection<object?> args,
            WorkflowQueryOptions? options = null)
        {
            QueryName = query;
            Options = options;
            return Task.FromResult((TResult)(object)42);
        }
    }
}
