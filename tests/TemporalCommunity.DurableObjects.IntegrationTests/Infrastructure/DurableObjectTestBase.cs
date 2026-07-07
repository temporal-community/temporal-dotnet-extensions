using Temporalio.Client;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;

/// <summary>
/// Base class for all integration scenario tests. Receives the shared WorkflowEnvironment fixture
/// via xUnit's ICollectionFixture mechanism — the environment boots once per test run.
/// Each test calls UniqueTaskQueue() to get an isolated task queue, preventing history cross-contamination.
/// </summary>
[Collection("WorkflowEnvironment")]
public abstract class DurableObjectTestBase
{
    protected WorkflowEnvironmentFixture Fixture { get; }
    protected ITemporalClient Client => Fixture.Env.Client;

    protected DurableObjectTestBase(WorkflowEnvironmentFixture fixture)
    {
        Fixture = fixture;
    }

    /// <summary>
    /// Returns a unique task queue name for use within a single test. Each test gets its own
    /// task queue so workers and workflow histories are fully isolated.
    /// </summary>
    protected static string UniqueTaskQueue() => $"tq-{Guid.NewGuid():N}";
}
