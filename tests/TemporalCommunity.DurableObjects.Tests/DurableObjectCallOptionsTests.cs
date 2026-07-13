using FakeItEasy;
using Temporalio.Client;
using Temporalio.Workflows;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

public sealed class DurableObjectCallOptionsTests
{
    [Fact]
    public async Task GetOrCreateAsync_PropagatesRpcOptions()
    {
        var client = A.Fake<ITemporalClient>();
        WorkflowOptions? captured = null;
        A.CallTo(() => client.StartWorkflowAsync(
                A<string>._,
                A<IReadOnlyCollection<object?>>._,
                A<WorkflowOptions>._))
            .Invokes(call => captured = call.GetArgument<WorkflowOptions>(2))
            .Returns(Task.FromResult(new WorkflowHandle(client, "object-id")));
        var options = CreateOptions();
        var factory = new DurableObjectFactory(client, "queue");

        await factory.GetOrCreateAsync<ITestDurableObject>("object-id", options);

        AssertRpcOptions(options, captured!.Rpc!);
    }

    [Fact]
    public async Task QueryDurableObjectAsync_PropagatesRpcOptions()
    {
        var client = A.Fake<ITemporalClient>();
        var handle = new CapturingWorkflowHandle(client);
        A.CallTo(() => client.GetWorkflowHandle(A<string>._, A<string?>._, A<string?>._))
            .Returns(handle);
        var options = CreateOptions();
        var factory = new DurableObjectFactory(client, "queue");

        var result = await factory.QueryDurableObjectAsync<int>(
            "object-id", "GetValue", null, options);

        Assert.Equal(42, result);
        AssertRpcOptions(options, handle.Options!.Rpc!);
    }

    [Fact]
    public async Task ProxyUpdate_PropagatesRpcOptions()
    {
        var client = A.Fake<ITemporalClient>();
        WorkflowStartUpdateWithStartOptions? captured = null;
        A.CallTo(() => client.StartUpdateWithStartWorkflowAsync<int>(
                A<string>._,
                A<IReadOnlyCollection<object?>>._,
                A<WorkflowStartUpdateWithStartOptions>._))
            .Invokes(call => captured = call.GetArgument<WorkflowStartUpdateWithStartOptions>(2))
            .ThrowsAsync(new InvalidOperationException("stop after capture"));
        var options = CreateOptions();
        var proxy = DurableObjectProxy<ITestDurableObject>.Create(
            client, "object-id", "queue", options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => proxy.IncrementAsync());

        Assert.Equal("stop after capture", exception.Message);
        AssertRpcOptions(options, captured!.Rpc!);
    }

    [Fact]
    public void Constructor_SnapshotsCallerOwnedMetadata()
    {
        var metadata = new Dictionary<string, string> { ["authorization"] = "original" };
        var bytes = new byte[] { 1, 2, 3 };
        var binaryMetadata = new Dictionary<string, byte[]> { ["trace-bin"] = bytes };

        var options = new DurableObjectCallOptions(
            metadata: metadata,
            binaryMetadata: binaryMetadata);
        metadata["authorization"] = "changed";
        bytes[0] = 9;
        binaryMetadata["trace-bin"] = new byte[] { 8 };
        options.BinaryMetadata!["trace-bin"][1] = 9;

        Assert.Equal("original", options.Metadata!["authorization"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, options.BinaryMetadata!["trace-bin"]);
    }

    private static DurableObjectCallOptions CreateOptions() => new(
        rpcTimeout: TimeSpan.FromSeconds(12),
        retry: false,
        metadata: new Dictionary<string, string> { ["authorization"] = "Bearer test" },
        binaryMetadata: new Dictionary<string, byte[]> { ["trace-bin"] = new byte[] { 4, 2 } },
        cancellationToken: new CancellationToken(canceled: true));

    private static void AssertRpcOptions(DurableObjectCallOptions expected, RpcOptions actual)
    {
        Assert.Equal(expected.CancellationToken, actual.CancellationToken);
        Assert.Equal(expected.RpcTimeout, actual.Timeout);
        Assert.Equal(expected.Retry, actual.Retry);
        Assert.Equal("Bearer test", actual.Metadata!.Single(pair => pair.Key == "authorization").Value);
        Assert.Equal(
            expected.BinaryMetadata!["trace-bin"],
            actual.BinaryMetadata!.Single(pair => pair.Key == "trace-bin").Value);
    }

    [Workflow]
    private interface ITestDurableObject : IDurableObject
    {
        [WorkflowUpdate]
        Task<int> IncrementAsync();
    }

    private sealed record CapturingWorkflowHandle(ITemporalClient Client)
        : WorkflowHandle(Client, "object-id")
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
