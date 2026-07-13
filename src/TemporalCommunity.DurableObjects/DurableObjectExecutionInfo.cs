using Temporalio.Api.Enums.V1;

namespace TemporalCommunity.DurableObjects;

/// <summary>Visibility metadata for one DurableObject workflow execution.</summary>
/// <param name="ObjectId">The namespace-wide Temporal workflow ID.</param>
/// <param name="RunId">The workflow run ID.</param>
/// <param name="WorkflowType">The registered Temporal workflow type.</param>
/// <param name="Status">The current execution status.</param>
/// <param name="TaskQueue">The execution's task queue.</param>
/// <param name="StartTime">When the execution was created.</param>
/// <param name="CloseTime">When the execution closed, if closed.</param>
/// <param name="HistoryLength">The number of events currently recorded in history.</param>
/// <param name="ScheduleId">The originating Temporal Schedule ID, or null for a canonical object.</param>
public sealed record DurableObjectExecutionInfo(
    string ObjectId,
    string RunId,
    string WorkflowType,
    WorkflowExecutionStatus Status,
    string TaskQueue,
    DateTime StartTime,
    DateTime? CloseTime,
    long HistoryLength,
    string? ScheduleId)
{
    /// <summary>Gets whether this execution was created by a Temporal Schedule.</summary>
    public bool IsScheduled => ScheduleId is not null;
}
