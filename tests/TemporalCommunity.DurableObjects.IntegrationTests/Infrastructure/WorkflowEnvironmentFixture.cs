using Temporalio.Testing;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;

/// <summary>
/// Boots a single local Temporal test server for the entire integration test collection.
/// WorkflowEnvironment.StartLocalAsync() downloads and starts the Temporal test server binary
/// on first run — shared across all scenario tests to avoid the per-class startup cost.
/// </summary>
public sealed class WorkflowEnvironmentFixture : IAsyncLifetime
{
    public WorkflowEnvironment Env { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Env = await WorkflowEnvironment.StartLocalAsync();
    }

    public async Task DisposeAsync()
    {
        await Env.ShutdownAsync();
    }
}

[CollectionDefinition("WorkflowEnvironment")]
public sealed class WorkflowEnvironmentCollection : ICollectionFixture<WorkflowEnvironmentFixture> { }
