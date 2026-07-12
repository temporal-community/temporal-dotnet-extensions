using FakeItEasy;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Workflows;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

public sealed class DurableObjectFactoryCancellationTests
{
    [Fact]
    public async Task GetOrCreateAsync_PassesCancellationTokenToStartRpc()
    {
        var client = A.Fake<ITemporalClient>();
        WorkflowOptions? capturedOptions = null;
        A.CallTo(() => client.StartWorkflowAsync(
                A<string>._,
                A<IReadOnlyCollection<object?>>._,
                A<WorkflowOptions>._))
            .Invokes(call => capturedOptions = call.GetArgument<WorkflowOptions>(2))
            .Returns(Task.FromResult(new WorkflowHandle(client, "object-id")));
        var factory = new DurableObjectFactory(client, "task-queue");
        using var cancellation = new CancellationTokenSource();

        await factory.GetOrCreateAsync<ITestDurableObject>("object-id", cancellation.Token);

        Assert.Equal(cancellation.Token, capturedOptions!.Rpc!.CancellationToken);
    }

    [Fact]
    public async Task QueryDurableObjectAsync_PassesCancellationTokenToQueryRpc()
    {
        var (factory, handle) = CreateFactoryWithCapturingHandle();
        using var cancellation = new CancellationTokenSource();

        var result = await factory.QueryDurableObjectAsync<int>(
            "object-id", "GetValue", cancellationToken: cancellation.Token);
        var options = handle.Options!;

        Assert.Equal(42, result);
        Assert.Equal(QueryRejectCondition.NotOpen, options.RejectCondition);
        Assert.Equal(cancellation.Token, options.Rpc!.CancellationToken);
    }

    [Fact]
    public async Task QueryOrDefaultAsync_PassesCancellationTokenToQueryRpc()
    {
        var (factory, handle) = CreateFactoryWithCapturingHandle();
        using var cancellation = new CancellationTokenSource();

        var result = await (factory.QueryOrDefaultAsync<int>(
            "object-id", "GetValue", cancellationToken: cancellation.Token)!);
        var options = handle.Options!;

        Assert.Equal(42, result);
        Assert.Equal(cancellation.Token, options.Rpc!.CancellationToken);
    }

    private static (DurableObjectFactory Factory, CapturingWorkflowHandle Handle)
        CreateFactoryWithCapturingHandle()
    {
        var client = A.Fake<ITemporalClient>();
        var handle = new CapturingWorkflowHandle(client);
        A.CallTo(() => client.GetWorkflowHandle(A<string>._, A<string?>._, A<string?>._))
            .Returns(handle);
        return (new DurableObjectFactory(client, "task-queue"), handle);
    }

    [Workflow]
    private interface ITestDurableObject : IDurableObject;

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
