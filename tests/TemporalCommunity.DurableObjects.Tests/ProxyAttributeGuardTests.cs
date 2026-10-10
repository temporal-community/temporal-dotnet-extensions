using FakeItEasy;
using System.Reflection;
using Temporalio.Client;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

/// <summary>
/// Tests that DurableObjectProxy{T}.Create() validates the interface attribute contract at
/// proxy-creation time (Bug 4 fix B). Uses FakeItEasy to provide the ITemporalClient stub
/// required by the proxy constructor — the client is not called during these tests.
/// </summary>
public sealed class ProxyAttributeGuardTests
{
    private readonly ITemporalClient _fakeClient = A.Fake<ITemporalClient>();

    // A valid interface: Task method marked [WorkflowUpdate] — no throw.
    [Fact]
    public void ValidInterface_WithUpdate_DoesNotThrow()
    {
        var ex = Record.Exception(() => CreateProxy<IValidUpdate>());
        Assert.Null(ex);
    }

    // A valid interface: non-Task method marked [WorkflowQuery] — no throw.
    [Fact]
    public void ValidInterface_WithQuery_DoesNotThrow()
    {
        var ex = Record.Exception(() => CreateProxy<IValidQuery>());
        Assert.Null(ex);
    }

    // Task method without [WorkflowUpdate] → InvalidOperationException.
    [Fact]
    public void TaskMethod_MissingUpdate_ThrowsInvalidOperation()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CreateProxy<ITaskWithoutUpdate>());
        Assert.Contains("MissingUpdateAsync", ex.Message, StringComparison.Ordinal);
        Assert.Contains("[WorkflowUpdate]", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SignalMethod_IsAccepted()
    {
        Assert.NotNull(CreateProxy<IWithSignal>());
    }

    // DeactivateAsync inherited from IDurableObject carries [WorkflowUpdate] — no throw.
    [Fact]
    public void DeactivateAsync_IsWorkflowUpdate_DoesNotThrow()
    {
        // IDurableObject.DeactivateAsync carries [WorkflowUpdate] — proxy guard must accept it.
        var ex = Record.Exception(() => CreateProxy<IMinimalDurableObject>());
        Assert.Null(ex);
    }

    // --- helper — uses InternalsVisibleTo to call DurableObjectProxy<T>.Create() ---

    private T CreateProxy<T>() where T : IDurableObject =>
        DurableObjectProxy<T>.Create(_fakeClient, "test-id", "test-queue");

    // --- test interfaces ---

    [Workflow]
    private interface IValidUpdate : IDurableObject
    {
        [WorkflowUpdate] Task DoWorkAsync();
    }

    [Workflow]
    private interface IValidQuery : IDurableObject
    {
        [WorkflowQuery] int GetValue();
    }

    [Workflow]
    private interface ITaskWithoutUpdate : IDurableObject
    {
        // Missing [WorkflowUpdate] — should be rejected.
        Task MissingUpdateAsync();
    }

    [Workflow]
    private interface IWithSignal : IDurableObject
    {
        [WorkflowSignal] Task SomeSignalAsync();
    }

    [Workflow]
    private interface IMinimalDurableObject : IDurableObject
    {
        // No extra methods — only inherits DeactivateAsync from IDurableObject.
    }
}
