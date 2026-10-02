using Temporalio.Activities;

namespace GeneratedNamespacePrefix.Shared.Activities;

/// <summary>
/// A starter activity. Activities can do I/O and use DI;
/// see https://docs.temporal.io/develop/dotnet/activities/basics.
/// </summary>
public sealed class SampleActivities
{
    [Activity]
    public Task<string> GreetAsync(string name) => Task.FromResult($"Hello, {name}!");
}
