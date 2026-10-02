#pragma warning disable CA1822 // Workflow methods must be instance methods
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.GettingStarted.Activities;

namespace TemporalCommunity.DurableObjects.GettingStarted.Objects;

public sealed record PageCounterState(int Count);

/// <summary>
/// Concrete DurableObject that tracks page views for a URL slug.
/// Demonstrates: WorkflowInit constructor, activity calls, OnActivateAsync, and the
/// required [WorkflowRun] boilerplate.
/// </summary>
[Workflow]
public sealed class PageCounter : DurableObjectBase<PageCounterState>, IPageCounter
{
    /// <summary>
    /// Restores the typed state snapshot after Continue-as-New, or creates cold-start state.
    /// </summary>
    [WorkflowInit]
    public PageCounter(DurableObjectSnapshot<PageCounterState>? snapshot = null)
        : base(snapshot, new PageCounterState(0)) { }

    /// <summary>
    /// Required boilerplate on every concrete DurableObject.
    /// Temporal does not inherit [WorkflowRun] — it must be declared on the concrete type.
    /// </summary>
    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<PageCounterState>? snapshot = null) => DurableObjectRunAsync();

    /// <inheritdoc/>
    protected override Task OnActivateAsync()
    {
        // Workflow.Logger is replay-safe; use it (not ILogger) inside workflow code.
        Workflow.Logger.LogInformation("PageCounter {Slug} activated", WorkflowId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    [WorkflowUpdate]
    public async Task IncrementAsync()
    {
        var nextCount = State.Count + 1;
        // Activities are the only place that can do I/O.
        // StartToCloseTimeout is required on every ActivityOptions.
        // Awaiting the DurableObjectBase helper keeps the call site concise; never use
        // ConfigureAwait(false) in workflow code.
        await ExecuteActivityAsync(
            (PageCounterActivities act) => act.RecordViewAsync(WorkflowId, nextCount),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });
        State = State with { Count = nextCount };
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public int GetCount() => State.Count;

    /// <inheritdoc/>
    [WorkflowQuery]
    public string GetSlug() => WorkflowId;
}
