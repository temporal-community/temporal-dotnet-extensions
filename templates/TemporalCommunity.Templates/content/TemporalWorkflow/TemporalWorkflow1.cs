using Temporalio.Workflows;

namespace TemplateNamespace;

[Workflow]
public sealed class TemporalWorkflow1
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
