using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.GettingStarted.Objects;

/// <summary>
/// Contract for a per-URL page-view counter.
/// The [Workflow] attribute is required — Temporal uses it for type resolution.
/// </summary>
[Workflow]
public interface IPageCounter : IDurableObject
{
    /// <summary>Records one page view. Calls an activity to persist the new count.</summary>
    [WorkflowUpdate]
    Task IncrementAsync();

    /// <summary>Returns the current view count (read-only, no await).</summary>
    [WorkflowQuery]
    int GetCount();

    /// <summary>Returns the URL slug this counter tracks.</summary>
    [WorkflowQuery]
    string GetSlug();
}
