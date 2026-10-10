using FakeItEasy;
using Temporalio.Client;
using Temporalio.Workflows;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

public sealed class ProxySignalTests
{
    [Fact]
    public async Task SignalWithStart_PreservesExplicitNamesArgumentsAndDefaultRpcOptions()
    {
        var client = A.Fake<ITemporalClient>();
        WorkflowOptions? captured = null;
        A.CallTo(() => client.StartWorkflowAsync(
                A<string>._, A<IReadOnlyCollection<object?>>._, A<WorkflowOptions>._))
            .Invokes(call =>
            {
                Assert.Equal("signal-workflow", call.GetArgument<string>(0));
                Assert.Empty(call.GetArgument<IReadOnlyCollection<object?>>(1)!);
                captured = call.GetArgument<WorkflowOptions>(2);
            })
            .Returns(Task.FromResult(new WorkflowHandle(client, "signal-id")));
        using var cts = new CancellationTokenSource();
        var rpc = new DurableObjectCallOptions(
            rpcTimeout: TimeSpan.FromSeconds(4), retry: false,
            metadata: new Dictionary<string, string> { ["test"] = "value" },
            binaryMetadata: new Dictionary<string, byte[]> { ["test-bin"] = [1, 2] },
            cancellationToken: cts.Token);
        var proxy = DurableObjectProxy<ISignalContract>.Create(client, "signal-id", "signal-queue", rpc);

        await proxy.AppendAsync(7, null);

        Assert.Equal("signal-id", captured!.Id);
        Assert.Equal("signal-queue", captured.TaskQueue);
        Assert.Equal("append-wire", captured.StartSignal);
        Assert.Equal(new object?[] { 7, null }, captured.StartSignalArgs);
        Assert.Equal(TimeSpan.FromSeconds(4), captured.Rpc!.Timeout);
        Assert.False(captured.Rpc.Retry);
        Assert.Equal(cts.Token, captured.Rpc.CancellationToken);
        Assert.Equal("value", Assert.Single(captured.Rpc.Metadata!).Value);
        Assert.Equal(new byte[] { 1, 2 }, Assert.Single(captured.Rpc.BinaryMetadata!).Value);

        await proxy.WakeAsync();
        Assert.Equal("Wake", captured.StartSignal);
        Assert.Empty(captured.StartSignalArgs!);
    }

    [Fact]
    public void GenericTaskSignal_IsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            DurableObjectProxy<IGenericSignal>.Create(A.Fake<ITemporalClient>(), "id", "queue"));

    [Fact]
    public void SignalAndUpdateOnSameMethod_IsRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            DurableObjectProxy<IMixedSignal>.Create(A.Fake<ITemporalClient>(), "id", "queue"));

    [Workflow("signal-workflow")]
    private interface ISignalContract : IDurableObject
    {
        [WorkflowSignal("append-wire")] Task AppendAsync(int value, string? note);
        [WorkflowSignal] Task WakeAsync();
    }

    [Workflow]
    private interface IGenericSignal : IDurableObject
    {
        [WorkflowSignal] Task<int> BadAsync();
    }

    [Workflow]
    private interface IMixedSignal : IDurableObject
    {
        [WorkflowSignal, WorkflowUpdate] Task BadAsync();
    }
}
