using Temporalio.Workflows;
using GeneratedNamespacePrefix.Shared.Activities;

namespace GeneratedNamespacePrefix.Shared.Workflows;

/// <summary>
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
