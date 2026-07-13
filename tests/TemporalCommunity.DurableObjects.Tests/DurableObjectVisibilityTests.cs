using FakeItEasy;
using Google.Protobuf.WellKnownTypes;
using System.Reflection;
using Temporalio.Api.Enums.V1;
using Temporalio.Api.Workflow.V1;
using Temporalio.Client;
using Temporalio.Common;
using Temporalio.Converters;
using Temporalio.Workflows;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

public sealed class DurableObjectVisibilityTests
{
    [Fact]
    public async Task RichListingDefaultsToCanonicalRunningAndMapsMetadata()
    {
        var client = A.Fake<ITemporalClient>();
        string? capturedQuery = null;
        var execution = CreateExecution("canonical-id", scheduleId: null);
        A.CallTo(() => client.ListWorkflowsAsync(A<string>._, A<WorkflowListOptions?>._))
            .Invokes(call => capturedQuery = call.GetArgument<string>(0))
            .Returns(ToAsyncEnumerable(execution));
        var factory = new DurableObjectFactory(client, "queue");

        var results = await CollectAsync(
            factory.ListDurableObjectExecutionsAsync<IVisibilityObject>());

        var result = Assert.Single(results);
        Assert.Contains("WorkflowType = 'VisibilityObject'", capturedQuery, StringComparison.Ordinal);
        Assert.Contains("ExecutionStatus = 'Running'", capturedQuery, StringComparison.Ordinal);
        Assert.Contains("TemporalScheduledById IS NULL", capturedQuery, StringComparison.Ordinal);
        Assert.Equal("canonical-id", result.ObjectId);
        Assert.Equal("run-id", result.RunId);
        Assert.Equal("visibility-queue", result.TaskQueue);
        Assert.Equal(12, result.HistoryLength);
        Assert.False(result.IsScheduled);
    }

    [Fact]
    public async Task RichListingCanIncludeAndClassifyScheduledExecutions()
    {
        var client = A.Fake<ITemporalClient>();
        string? capturedQuery = null;
        var execution = CreateExecution("scheduled-id", "nightly-schedule");
        A.CallTo(() => client.ListWorkflowsAsync(A<string>._, A<WorkflowListOptions?>._))
            .Invokes(call => capturedQuery = call.GetArgument<string>(0))
            .Returns(ToAsyncEnumerable(execution));
        var factory = new DurableObjectFactory(client, "queue");

        var results = await CollectAsync(factory.ListDurableObjectExecutionsAsync<IVisibilityObject>(
            new DurableObjectListOptions(runningOnly: false, includeScheduled: true)));

        var result = Assert.Single(results);
        Assert.DoesNotContain("ExecutionStatus", capturedQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("TemporalScheduledById IS NULL", capturedQuery, StringComparison.Ordinal);
        Assert.True(result.IsScheduled);
        Assert.Equal("nightly-schedule", result.ScheduleId);
    }

    [Fact]
    public async Task ExistingIdListingPreservesScheduledExecutionBehavior()
    {
        var client = A.Fake<ITemporalClient>();
        string? capturedQuery = null;
        A.CallTo(() => client.ListWorkflowsAsync(A<string>._, A<WorkflowListOptions?>._))
            .Invokes(call => capturedQuery = call.GetArgument<string>(0))
            .Returns(ToAsyncEnumerable(CreateExecution("scheduled-id", "schedule")));
        var factory = new DurableObjectFactory(client, "queue");

        var results = await CollectAsync(factory.ListDurableObjectsAsync<IVisibilityObject>());

        Assert.Equal("scheduled-id", Assert.Single(results));
        Assert.DoesNotContain("TemporalScheduledById IS NULL", capturedQuery, StringComparison.Ordinal);
    }

    private static Temporalio.Client.WorkflowExecution CreateExecution(string id, string? scheduleId)
    {
        var searchAttributes = new SearchAttributeCollection.Builder();
        if (scheduleId is not null)
        {
            searchAttributes.Set(
                SearchAttributeKey.CreateKeyword("TemporalScheduledById"), scheduleId);
        }

        var start = DateTime.SpecifyKind(new DateTime(2026, 7, 12, 12, 0, 0), DateTimeKind.Utc);
        var raw = new WorkflowExecutionInfo
        {
            Execution = new Temporalio.Api.Common.V1.WorkflowExecution
            {
                WorkflowId = id,
                RunId = "run-id",
            },
            Type = new Temporalio.Api.Common.V1.WorkflowType { Name = "VisibilityObject" },
            Status = WorkflowExecutionStatus.Running,
            TaskQueue = "visibility-queue",
            StartTime = Timestamp.FromDateTime(start),
            HistoryLength = 12,
            SearchAttributes = searchAttributes.ToSearchAttributeCollection().ToProto(),
        };
        return (Temporalio.Client.WorkflowExecution)Activator.CreateInstance(
            typeof(Temporalio.Client.WorkflowExecution),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: new object?[] { raw, DataConverter.Default, "default" },
            culture: null)!;
    }

    private static async IAsyncEnumerable<Temporalio.Client.WorkflowExecution> ToAsyncEnumerable(
        Temporalio.Client.WorkflowExecution execution)
    {
        yield return execution;
        await Task.CompletedTask;
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> values)
    {
        var results = new List<T>();
        await foreach (var value in values)
        {
            results.Add(value);
        }

        return results;
    }

    [Workflow]
    private interface IVisibilityObject : IDurableObject;
}
