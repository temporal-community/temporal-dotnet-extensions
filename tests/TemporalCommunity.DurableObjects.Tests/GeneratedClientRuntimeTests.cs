using FakeItEasy;
using Temporalio.Client;
using Temporalio.Workflows;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

public sealed class GeneratedClientRuntimeTests
{
    static GeneratedClientRuntimeTests() =>
        DurableObjectGeneratedClientRegistry.Register<IGeneratedObject>(
            invoker => new GeneratedObjectClient(invoker));

    [Fact]
    public async Task FactoryPrefersRegisteredClientAndInvokerPropagatesOptions()
    {
        var client = A.Fake<ITemporalClient>();
        WorkflowStartUpdateWithStartOptions? captured = null;
        A.CallTo(() => client.StartUpdateWithStartWorkflowAsync<int>(
                A<string>._,
                A<IReadOnlyCollection<object?>>._,
                A<WorkflowStartUpdateWithStartOptions>._))
            .Invokes(call => captured = call.GetArgument<WorkflowStartUpdateWithStartOptions>(2))
            .ThrowsAsync(new InvalidOperationException("captured"));
        using var cancellation = new CancellationTokenSource();
        var options = new DurableObjectCallOptions(
            rpcTimeout: TimeSpan.FromSeconds(3),
            metadata: new Dictionary<string, string> { ["authorization"] = "Bearer generated" },
            cancellationToken: cancellation.Token);
        var factory = new DurableObjectFactory(client, "queue");

        var generated = factory.Get<IGeneratedObject>("generated-id", options);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => generated.AddAsync(2));

        Assert.IsType<GeneratedObjectClient>(generated);
        Assert.Equal("captured", exception.Message);
        Assert.Equal(options.RpcTimeout, captured!.Rpc!.Timeout);
        Assert.Equal(options.CancellationToken, captured.Rpc.CancellationToken);
        Assert.Equal(
            "Bearer generated",
            captured.Rpc.Metadata!.Single(pair => pair.Key == "authorization").Value);
    }

    [Fact]
    public async Task GeneratedQueryUsesAsyncInvokerAndPerCallOverride()
    {
        var client = A.Fake<ITemporalClient>();
        var handle = new CapturingWorkflowHandle(client);
        A.CallTo(() => client.GetWorkflowHandle(A<string>._, A<string?>._, A<string?>._))
            .Returns(handle);
        var factory = new DurableObjectFactory(client, "queue");
        var generated = Assert.IsType<GeneratedObjectClient>(
            factory.Get<IGeneratedObject>("generated-id"));
        var options = new DurableObjectCallOptions(rpcTimeout: TimeSpan.FromSeconds(7));

        var result = await generated.GetValueAsync(options);

        Assert.Equal(42, result);
        Assert.Equal(options.RpcTimeout, handle.Options!.Rpc!.Timeout);
    }

    [Fact]
    public void FactoryRetainsDispatchProxyFallbackForUnregisteredContract()
    {
        var factory = new DurableObjectFactory(A.Fake<ITemporalClient>(), "queue");

        var client = factory.Get<IUngeneratedObject>("fallback-id");

        Assert.IsAssignableFrom<DurableObjectProxy<IUngeneratedObject>>(client);
    }

    [Workflow]
    private interface IGeneratedObject : IDurableObject
    {
        [WorkflowUpdate]
        Task<int> AddAsync(int amount);

        [WorkflowQuery]
        int GetValue();
    }

    [Workflow]
    private interface IUngeneratedObject : IDurableObject
    {
        [WorkflowQuery]
        int GetValue();
    }

    private sealed class GeneratedObjectClient : IGeneratedObject
    {
        private readonly DurableObjectClientInvoker _invoker;

        internal GeneratedObjectClient(DurableObjectClientInvoker invoker) => _invoker = invoker;

        public Task<int> AddAsync(int amount) =>
            _invoker.ExecuteUpdateAsync<int>("Add", new object?[] { amount });

        public Task DeactivateAsync() =>
            _invoker.ExecuteUpdateAsync("Deactivate", Array.Empty<object?>());

        public int GetValue() => GetValueAsync().GetAwaiter().GetResult();

        internal Task<int> GetValueAsync() =>
            _invoker.QueryAsync<int>("GetValue", Array.Empty<object?>());

        internal Task<int> GetValueAsync(DurableObjectCallOptions callOptions) =>
            _invoker.QueryAsync<int>("GetValue", Array.Empty<object?>(), callOptions);
    }

    private sealed record CapturingWorkflowHandle(ITemporalClient Client)
        : WorkflowHandle(Client, "generated-id")
    {
        internal WorkflowQueryOptions? Options { get; private set; }

        public override Task<TResult> QueryAsync<TResult>(
            string query,
            IReadOnlyCollection<object?> args,
            WorkflowQueryOptions? options = null)
        {
            Options = options;
            return Task.FromResult((TResult)(object)42);
        }
    }
}
