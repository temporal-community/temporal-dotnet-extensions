#pragma warning disable CA1822 // Workflow methods must be instance methods
#pragma warning disable CA2007 // ConfigureAwait — workflow code must use ConfigureAwait(true), never false
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.GettingStarted.Activities;

namespace TemporalCommunity.DurableObjects.GettingStarted.Objects;

/// <summary>
/// Concrete DurableObject that tracks page views for a URL slug.
/// Demonstrates: WorkflowInit constructor, activity calls, OnActivateAsync, and the
/// required [WorkflowRun] boilerplate.
/// </summary>
[Workflow]
public sealed class PageCounter : DurableObjectBase, IPageCounter
{
    private readonly string _slug;
    private int _count;

    /// <summary>
    /// Constructor called by Temporal on every new execution and after ContinueAsNew.
    /// The [WorkflowInit] attribute tells the SDK to call this constructor with the
    /// workflow's start arguments.
    /// </summary>
    [WorkflowInit]
    public PageCounter(string slug) => _slug = slug;

    /// <summary>
    /// Required boilerplate on every concrete DurableObject.
    /// Temporal does not inherit [WorkflowRun] — it must be declared on the concrete type.
    /// </summary>
    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    /// <inheritdoc/>
    protected override Task OnActivateAsync()
    {
        // Workflow.Logger is replay-safe; use it (not ILogger) inside workflow code.
        Workflow.Logger.LogInformation("PageCounter {Slug} activated", _slug);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    [WorkflowUpdate]
    public async Task IncrementAsync()
    {
        _count++;
        // Activities are the only place that can do I/O.
        // StartToCloseTimeout is required on every ActivityOptions.
        // ConfigureAwait(true) keeps the continuation on the workflow scheduler.
        await Workflow.ExecuteActivityAsync(
            (PageCounterActivities act) => act.RecordViewAsync(_slug, _count),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) })
            .ConfigureAwait(true);
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public int GetCount() => _count;

    /// <inheritdoc/>
    [WorkflowQuery]
    public string GetSlug() => _slug;
}
