using Temporalio.Workflows;

namespace Fixtures.HostProject;

[Workflow]
public sealed class CustomWorkflowName
{
    [WorkflowRun]
    public async Task<string> RunAsync(string input)
    {
        // Workflows must be deterministic; use Workflow.* APIs for time and async coordination.
        // See https://docs.temporal.io/develop/dotnet/workflows/basics.
        await Workflow.DelayAsync(TimeSpan.FromSeconds(1));
        return input;
    }
}
