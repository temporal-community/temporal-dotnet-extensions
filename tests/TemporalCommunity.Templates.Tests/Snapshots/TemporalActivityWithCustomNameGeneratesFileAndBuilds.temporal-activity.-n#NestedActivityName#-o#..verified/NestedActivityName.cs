using Temporalio.Activities;

namespace Fixtures.HostProject;

public sealed class NestedActivityName
{
    // Activities can do I/O and use DI. See https://docs.temporal.io/develop/dotnet/activities/basics.
    //
    // The method name is generated from the class name to keep Temporal activity type names unique.
    [Activity]
    public async Task<string> NestedActivityNameAsync(string input)
    {
        // Replace with real activity logic.
        await Task.CompletedTask;
        return input;
    }
}
