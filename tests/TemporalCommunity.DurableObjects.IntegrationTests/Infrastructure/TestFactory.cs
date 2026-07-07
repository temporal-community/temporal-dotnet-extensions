using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;

/// <summary>
/// Creates an IDurableObjectFactory for use in integration tests without requiring the full
/// hosted worker DI pipeline. Uses the DI extension method to construct the factory correctly.
/// </summary>
public static class TestFactory
{
    /// <summary>
    /// Creates an IDurableObjectFactory wired to the given client and task queue.
    /// </summary>
    public static IDurableObjectFactory Create(ITemporalClient client, string taskQueue)
    {
        var services = new ServiceCollection();
        services.AddSingleton(client);
        services.AddDurableObjects(taskQueue);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IDurableObjectFactory>();
    }
}
