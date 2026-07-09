using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.Scheduling.Objects;

/// <summary>
/// Contract for a per-tick report generator.
/// Each Temporal Schedule tick creates a fresh execution with a time-suffixed ID.
/// The object activates, publishes the report, and self-deactivates.
/// </summary>
[Workflow]
public interface IReportGenerator : IDurableObject
{
    /// <summary>
    /// Manually triggers report generation (useful for ad-hoc runs outside the schedule).
    /// </summary>
    [WorkflowUpdate]
    Task GenerateAsync();

    /// <summary>Returns the workflow ID of this report execution (read-only, no await).</summary>
    [WorkflowQuery]
    string GetReportId();
}
