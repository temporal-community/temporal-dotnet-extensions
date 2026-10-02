using Temporalio.Workflows;
using GeneratedNamespacePrefix.Shared.Activities;

namespace GeneratedNamespacePrefix.Shared.Workflows;

/// <summary>
/// A starter workflow in Shared so Client can start it by type.
/// Workflows must be deterministic; see https://docs.temporal.io/develop/dotnet/workflows/basics.
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
