// TestHelper.cs — Lightweight factory and worker helpers for test setup.
//
// Why not use the hosted worker (IHostedService)?
//   The hosted worker is designed for long-running production services. In tests we need
//   fine-grained control: start the worker, run the test, stop the worker. TemporalWorker
//   (non-hosted) gives us that lifecycle. We dispose it in the test's finally block.
//
// Why AddDurableObjects (DI extension)?
//   The DurableObjectFactory requires an ITemporalClient and a default task queue. The
//   DI extension wires these together correctly, matching what production code does.
//   This ensures tests exercise the same factory construction path as real applications.
//
// Why pass typeof(TodoList).Assembly to AddDurableObjectWorkflows?
//   AddDurableObjectWorkflows scans the assembly for all concrete DurableObjectBase subclasses
//   that carry [Workflow], validates each one at registration time (catches missing [WorkflowRun],
//   bad signatures, etc.), and auto-installs DurableObjectWorkerInterceptor. Passing the sample
//   assembly here registers TodoList and sets up the full DurableObject runtime.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using Temporalio.Worker;

namespace TemporalCommunity.DurableObjects.Testing.Infrastructure;

/// <summary>
/// Convenience helpers for constructing the test worker and factory.
/// </summary>
public static class TestHelper
{
    /// <summary>
    /// Creates a <see cref="TemporalWorker"/> that registers all DurableObject types found
    /// in <paramref name="assembly"/> and installs <c>DurableObjectWorkerInterceptor</c>.
    /// The caller is responsible for disposing the returned worker.
    /// </summary>
    /// <param name="client">Temporal client from the shared fixture.</param>
    /// <param name="taskQueue">Unique task queue for this test (call UniqueTaskQueue()).</param>
    /// <param name="assembly">Assembly containing the concrete DurableObject types to register.</param>
    /// <returns>A configured worker. Wrap in a <c>using</c> statement.</returns>
    public static TemporalWorker CreateWorker(
        ITemporalClient client,
        string taskQueue,
        Assembly assembly)
    {
        var workerOptions = new TemporalWorkerOptions(taskQueue);

        // AddDurableObjectWorkflows does three things in one call:
        //   1. Scans the assembly and registers all concrete DurableObjectBase types that carry [Workflow].
        //   2. Validates each type at registration time (catches missing [WorkflowRun], etc.).
        //   3. Installs DurableObjectWorkerInterceptor — required for serialization, exception wrapping,
        //      and drain-window gating. Without it the DurableObject runtime is incomplete.
        workerOptions.AddDurableObjectWorkflows(assembly);

        return new TemporalWorker(client, workerOptions);
    }

    /// <summary>
    /// Creates an <see cref="IDurableObjectFactory"/> wired to the given client and task queue
    /// using the same DI extension path that production code uses.
    /// </summary>
    /// <param name="client">Temporal client from the shared fixture.</param>
    /// <param name="taskQueue">The task queue the worker is polling.</param>
    /// <returns>
    /// A tuple of (<see cref="IDurableObjectFactory"/>, <see cref="IServiceProvider"/>).
    /// The caller must dispose the <see cref="IServiceProvider"/> (typically in a finally block)
    /// to release resources held by the built container.
    /// </returns>
    public static (IDurableObjectFactory Factory, IServiceProvider Provider) CreateFactory(
        ITemporalClient client, string taskQueue)
    {
        var services = new ServiceCollection();
        services.AddSingleton(client);

        // AddDurableObjects registers IDurableObjectFactory as a singleton and records the
        // default task queue. Single-argument factory calls (Get<T>(id), GetOrCreateAsync<T>(id))
        // use this queue automatically.
        services.AddDurableObjects(taskQueue);

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IDurableObjectFactory>(), sp);
    }
}
