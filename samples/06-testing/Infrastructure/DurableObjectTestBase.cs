// DurableObjectTestBase.cs — Base class for all DurableObject integration tests.
//
// This class handles the boilerplate of receiving the shared fixture from xUnit and exposing
// a typed Temporal client. Each concrete test class derives from this.
//
// Why UniqueTaskQueue()?
//   Each test must run its worker on a distinct task queue. If two tests share a queue, their
//   workers compete for tasks and can observe each other's workflow history, causing flaky
//   non-determinism failures. Guid.NewGuid():N gives a short (32-char), URL-safe, unique name.

using Temporalio.Client;
using Xunit;

namespace TemporalCommunity.DurableObjects.Testing.Infrastructure;

/// <summary>
/// Abstract base providing a shared Temporal client and per-test task queue isolation.
/// Derive from this class in any test class that exercises DurableObjects end-to-end.
/// </summary>
[Collection("WorkflowEnvironment")]
public abstract class DurableObjectTestBase
{
    /// <summary>The shared test environment fixture.</summary>
    protected WorkflowEnvironmentFixture Fixture { get; }

    /// <summary>The Temporal client connected to the SDK-managed local server.</summary>
    protected ITemporalClient Client => Fixture.Env.Client;

    /// <summary>Receives the shared fixture from xUnit's collection fixture mechanism.</summary>
    protected DurableObjectTestBase(WorkflowEnvironmentFixture fixture)
    {
        Fixture = fixture;
    }

    /// <summary>
    /// Returns a unique task queue name for use within a single test.
    /// Each test gets its own queue so workers and workflow histories are fully isolated —
    /// no test can observe or interfere with another test's workflow execution.
    /// </summary>
    protected static string UniqueTaskQueue() => $"tq-{Guid.NewGuid():N}";
}
