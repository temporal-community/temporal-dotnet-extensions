using Temporalio.Activities;

namespace TemplateNamespace;

public sealed class TemporalActivity1
{
    // Activities CAN do I/O, use ILogger, and access DI (unlike workflow code, which must stay
    // deterministic) — they run outside the workflow scheduler.
    [Activity]
    public async Task<string> RunAsync(string input)
    {
        // Replace with real activity logic.
        await Task.CompletedTask;
        return input;
    }
}
