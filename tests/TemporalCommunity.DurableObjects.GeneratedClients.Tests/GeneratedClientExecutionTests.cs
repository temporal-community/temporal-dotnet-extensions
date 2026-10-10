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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmittedClientExecutesSignalWithExplicitWireNameAndArguments(bool withOptions)
    {
        var temporalClient = A.Fake<ITemporalClient>();
        WorkflowOptions? captured = null;
        A.CallTo(() => temporalClient.StartWorkflowAsync(
                A<string>._, A<IReadOnlyCollection<object?>>._, A<WorkflowOptions>._))
            .Invokes(call =>
            {
                Assert.Equal("GeneratedCounter", call.GetArgument<string>(0));
                Assert.Empty(call.GetArgument<IReadOnlyCollection<object?>>(1)!);
                captured = call.GetArgument<WorkflowOptions>(2);
            })
            .Returns(Task.FromResult(new WorkflowHandle(temporalClient, "counter-id")));
        var factory = new DurableObjectFactory(temporalClient, "generated-queue");
        using var cancellation = new CancellationTokenSource();
        var options = new DurableObjectCallOptions(
            rpcTimeout: TimeSpan.FromSeconds(7),
            retry: false,
            cancellationToken: cancellation.Token,
            metadata: new Dictionary<string, string> { ["test-header"] = "signal-value" });
        var generated = factory.GetGeneratedCounterClient("counter-id");

        if (withOptions)
        {
            await generated.SetValueAsync(5, null, options);
        }
        else
        {
            await ((IGeneratedCounter)generated).SetValueAsync(5, null);
        }

        Assert.Equal("counter-id", captured!.Id);
        Assert.Equal("generated-queue", captured.TaskQueue);
        Assert.Equal("set-value", captured.StartSignal);
        Assert.Equal(new object?[] { 5, null }, captured.StartSignalArgs);
        if (withOptions)
        {
            Assert.Equal(TimeSpan.FromSeconds(7), captured.Rpc!.Timeout);
            Assert.False(captured.Rpc.Retry);
            Assert.Equal(cancellation.Token, captured.Rpc.CancellationToken);
            Assert.Equal("signal-value", Assert.Single(captured.Rpc.Metadata!).Value);
        }
    }

    [Fact]
    public async Task EmittedClientStripsAsyncSignalSuffixAndSendsNoArguments()
    {
        var temporalClient = A.Fake<ITemporalClient>();
        WorkflowOptions? captured = null;
        A.CallTo(() => temporalClient.StartWorkflowAsync(
                A<string>._, A<IReadOnlyCollection<object?>>._, A<WorkflowOptions>._))
            .Invokes(call => captured = call.GetArgument<WorkflowOptions>(2))
            .Returns(Task.FromResult(new WorkflowHandle(temporalClient, "counter-id")));
        var factory = new DurableObjectFactory(temporalClient, "generated-queue");

        await factory.GetGeneratedCounterClient("counter-id").WakeAsync();

        Assert.Equal("Wake", captured!.StartSignal);
        Assert.Empty(captured.StartSignalArgs!);
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
