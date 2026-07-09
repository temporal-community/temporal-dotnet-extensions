// WorkflowEnvironmentFixture.cs — Shared Temporal test server for the entire test session.
//
// Why StartLocalAsync()?
//   WorkflowEnvironment.StartLocalAsync() boots an in-process Temporal test server that runs
//   the full Temporal state machine without requiring an external Docker container or running
//   Temporal CLI. This is the standard approach for integration testing .NET workflow code.
//   The binary is downloaded once (cached in ~/.temporalio) on first run.
//
// Why ICollectionFixture?
//   Starting the local server takes ~2-4 seconds. ICollectionFixture<T> shares one instance
//   across all tests in the collection, so the server starts once for the whole suite rather
//   than once per test class. This keeps the suite fast.
//
// Why IAsyncLifetime?
//   xUnit calls InitializeAsync before any tests run and DisposeAsync after all tests finish.
//   ShutdownAsync() cleanly stops the Temporal server and releases its port.

using Temporalio.Testing;
using Xunit;

namespace TemporalCommunity.DurableObjects.Testing.Infrastructure;

/// <summary>
/// Boots a single local Temporal test server for the entire test collection.
/// Shared via xUnit's ICollectionFixture to pay the startup cost only once per run.
/// </summary>
public sealed class WorkflowEnvironmentFixture : IAsyncLifetime
{
    /// <summary>The running Temporal test environment. Available after InitializeAsync completes.</summary>
    public WorkflowEnvironment Env { get; private set; } = null!;

    /// <summary>Starts the local Temporal test server.</summary>
    public async Task InitializeAsync()
    {
        Env = await WorkflowEnvironment.StartLocalAsync().ConfigureAwait(false);
    }

    /// <summary>Shuts down the test server and releases resources.</summary>
    public async Task DisposeAsync()
    {
        await Env.ShutdownAsync().ConfigureAwait(false);
    }
}

// CollectionDefinition links the fixture name to the fixture type.
// Test classes declare [Collection("WorkflowEnvironment")] to join this collection
// and receive the shared fixture via constructor injection.
[CollectionDefinition("WorkflowEnvironment")]
public sealed class WorkflowEnvironmentCollection : ICollectionFixture<WorkflowEnvironmentFixture> { }
