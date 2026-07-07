using System.Reflection;
using Temporalio.Client;
using Temporalio.Worker;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;

/// <summary>
/// Helper that creates a TemporalWorker for a given task queue with DurableObjectWorkerInterceptor
/// pre-installed. Each test calls Build() with a unique task queue (from UniqueTaskQueue()) and
/// the workflow/activity types needed for that scenario.
/// </summary>
public static class ScenarioWorkerBuilder
{
    /// <summary>
    /// Builds a TemporalWorker configured for DurableObject scenarios.
    /// Registers specified workflow types individually plus ReminderDispatcher (via library assembly
    /// scan), installs the DurableObjectWorkerInterceptor, and registers activity instances.
    /// </summary>
    /// <param name="client">Temporal client from the shared fixture.</param>
    /// <param name="taskQueue">Unique task queue for this test run.</param>
    /// <param name="workflowTypes">
    /// Concrete DurableObjectBase subclasses to register. Must NOT include abstract types or
    /// types with [WorkflowSignal].
    /// </param>
    /// <param name="activityInstances">Activity instances to register.</param>
    /// <param name="options">Optional DurableObjectWorkerOptions for the interceptor.</param>
    /// <returns>A configured TemporalWorker (caller must dispose).</returns>
    public static TemporalWorker Build(
        ITemporalClient client,
        string taskQueue,
        IEnumerable<Type>? workflowTypes = null,
        IEnumerable<object>? activityInstances = null,
        DurableObjectWorkerOptions? options = null)
    {
        var workerOptions = new TemporalWorkerOptions(taskQueue);

        // Register each concrete workflow type individually (precise control, no assembly scan).
        if (workflowTypes is not null)
        {
            foreach (var type in workflowTypes)
            {
                workerOptions.AddWorkflow(type);
            }
        }

        // Use AddDurableObjectWorkflows on an empty assembly to get: ReminderDispatcher + interceptor.
        // We scan the library assembly (TemporalCommunity.DurableObjects) which contains only
        // ReminderDispatcher as a concrete DurableObjectBase-derivative with [Workflow].
        // This gives us both ReminderDispatcher registration AND interceptor installation without
        // exposing the internal ReminderDispatcher type directly.
        var libraryAssembly = typeof(DurableObjectBase).Assembly;
        options ??= new DurableObjectWorkerOptions();
        workerOptions.AddDurableObjectWorkflows(libraryAssembly, options);

        // Register activity instances.
        if (activityInstances is not null)
        {
            foreach (var instance in activityInstances)
            {
                workerOptions.AddAllActivities(instance);
            }
        }

        return new TemporalWorker(client, workerOptions);
    }
}
