#pragma warning disable CA1822 // Workflow methods must be instance methods
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.Scheduling.Activities;

namespace TemporalCommunity.DurableObjects.Scheduling.Objects;

/// <summary>
/// Per-tick report generator. Each Temporal Schedule tick spawns a fresh, time-suffixed execution.
///
/// Pattern: Schedule (per-tick fresh execution)
/// - Each tick = a brand-new workflow execution with a unique ID (e.g., "report-gen-2026-07-08T10:00:00Z")
/// - The object does its work in OnActivateAsync, then calls Deactivate() to self-complete
/// - Without Deactivate(), ScheduleOverlapPolicy.Skip would suppress subsequent ticks while
///   the first execution remains open
/// - State does NOT accumulate across ticks — each execution starts with a clean slate
/// </summary>
[Workflow]
public sealed class ReportGenerator : DurableObjectBase, IReportGenerator
{
    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    /// <summary>
    /// On activation: publish the report via an activity, then self-deactivate.
    /// This is the canonical pattern for scheduled objects — all work happens here,
    /// and Deactivate() lets the next tick create a fresh execution.
    /// </summary>
    protected override async Task OnActivateAsync()
    {
        var reportId = WorkflowId;
        Workflow.Logger.LogInformation("ReportGenerator activated for execution {ReportId}", reportId);

        await ExecuteActivityAsync(
            (SchedulingActivities act) => act.PublishReportAsync(reportId),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });

        // Self-deactivate so the next scheduled tick can create a fresh execution.
        // Without this, ScheduleOverlapPolicy.Skip suppresses all subsequent ticks.
        // CA1849 suppressed: Deactivate() is a synchronous flag-setter, not a blocking call.
#pragma warning disable CA1849
        Deactivate();
#pragma warning restore CA1849
    }

    /// <inheritdoc/>
    [WorkflowUpdate]
    public async Task GenerateAsync()
    {
        var reportId = WorkflowId;
        await ExecuteActivityAsync(
            (SchedulingActivities act) => act.PublishReportAsync(reportId),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public string GetReportId() => WorkflowId;
}
