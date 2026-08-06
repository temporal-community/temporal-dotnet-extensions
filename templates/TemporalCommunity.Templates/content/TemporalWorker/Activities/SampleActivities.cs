using Temporalio.Activities;

namespace TemporalWorker1.Activities;

/// <summary>
/// A starter activity class. Activities (unlike workflow code) can freely do I/O, use
/// <c>ILogger</c>, and access DI — they run outside the workflow scheduler.
/// </summary>
public sealed class SampleActivities
{
    [Activity]
    public Task<string> GreetAsync(string name) => Task.FromResult($"Hello, {name}!");
}
