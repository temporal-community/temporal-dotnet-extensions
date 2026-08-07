using Temporalio.Workflows;
using GeneratedClassNamePrefix.Shared.Activities;

namespace GeneratedClassNamePrefix.Shared.Workflows;

/// <summary>
/// A starter workflow class. Replace with real workflow logic. Workflow code must be
/// deterministic: only use <c>Workflow.*</c> APIs for time, randomness, and async coordination
/// (never <c>DateTime.UtcNow</c>, <c>Task.Delay</c>, <c>Guid.NewGuid</c>, etc. directly). Declared
/// in Shared (not Worker) so Client can reference it for type-safe
/// <c>StartWorkflowAsync&lt;SampleWorkflow&gt;(...)</c> calls.
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
