using Temporalio.Workflows;

namespace TemplateNamespace;

[Workflow]
public sealed class TemporalWorkflow1
{
    [WorkflowRun]
    public async Task<string> RunAsync(string input)
    {
        // Replace with real workflow logic. Workflow code must be deterministic: only use
        // Workflow.* APIs for time, randomness, and async coordination (never DateTime.UtcNow,
        // Task.Delay, Guid.NewGuid, etc. directly).
        await Workflow.DelayAsync(TimeSpan.FromSeconds(1));
        return input;
    }
}
