using Temporalio.Workflows;
using TemporalWorker1.Activities;

namespace TemporalWorker1.Workflows;

/// <summary>
/// A starter workflow class. Replace with real workflow logic. Workflow code must be
/// deterministic: only use <c>Workflow.*</c> APIs for time, randomness, and async coordination
/// (never <c>DateTime.UtcNow</c>, <c>Task.Delay</c>, <c>Guid.NewGuid</c>, etc. directly).
/// </summary>
[Workflow]
public sealed class SampleWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string name) =>
        await Workflow.ExecuteActivityAsync(
            (SampleActivities activities) => activities.GreetAsync(name),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });
}
