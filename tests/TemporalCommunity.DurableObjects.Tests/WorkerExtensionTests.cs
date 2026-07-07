using Temporalio.Worker;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

/// <summary>
/// Tests for AddDurableObjectWorkflows assembly scan behavior. All assertions are structural
/// (no live Temporal server required).
/// </summary>
public sealed class WorkerExtensionTests
{
    // AddDurableObjectWorkflows scanning the library assembly registers ReminderDispatcher.
    [Fact]
    public void AddDurableObjectWorkflows_RegistersReminderDispatcherFromLibraryAssembly()
    {
        var options = new TemporalWorkerOptions("test-queue");
        // Scan the library assembly — its only concrete DurableObjectBase subclass is ReminderDispatcher.
        options.AddDurableObjectWorkflows(typeof(DurableObjectBase).Assembly);

        var registered = GetRegisteredWorkflowTypes(options);
        Assert.Contains(registered, t => t.Name == "ReminderDispatcher");
    }

    // Abstract DurableObjectBase is never registered as a workflow.
    [Fact]
    public void AddDurableObjectWorkflows_ExcludesAbstractBaseClass()
    {
        var options = new TemporalWorkerOptions("test-queue");
        options.AddDurableObjectWorkflows(typeof(DurableObjectBase).Assembly);

        var registered = GetRegisteredWorkflowTypes(options);
        Assert.DoesNotContain(typeof(DurableObjectBase), registered);
    }

    // Only DurableObjectBase subclasses appear in registered workflows from the library assembly.
    [Fact]
    public void AddDurableObjectWorkflows_RegistersOnlyDurableObjectBaseSubclasses()
    {
        var options = new TemporalWorkerOptions("test-queue");
        options.AddDurableObjectWorkflows(typeof(DurableObjectBase).Assembly);

        var registered = GetRegisteredWorkflowTypes(options);
        Assert.All(registered, t =>
            Assert.True(
                t.Name == "ReminderDispatcher" || typeof(DurableObjectBase).IsAssignableFrom(t),
                $"Unexpected type registered: {t.FullName}"));
    }

    // Types with [WorkflowSignal] throw InvalidOperationException during registration.
    [Fact]
    public void AddDurableObjectWorkflows_ThrowsForSignalMethods()
    {
        var options = new TemporalWorkerOptions("test-queue");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            options.AddDurableObjectWorkflows(typeof(SignalBearingWorkflow).Assembly));
        Assert.Contains("[WorkflowSignal]", ex.Message, StringComparison.Ordinal);
    }

    // ReminderDispatcher is always registered (internal framework workflow).
    [Fact]
    public void AddDurableObjectWorkflows_AlwaysRegistersReminderDispatcher()
    {
        var options = new TemporalWorkerOptions("test-queue");
        options.AddDurableObjectWorkflows(typeof(DurableObjectBase).Assembly);

        var registered = GetRegisteredWorkflowTypes(options);
        Assert.Contains(registered, t => t.Name == "ReminderDispatcher");
    }

    // DurableObjectWorkerOptions defaults: Serialize == true, Authorize == null.
    [Fact]
    public void DurableObjectWorkerOptions_HasCorrectDefaults()
    {
        var opts = new DurableObjectWorkerOptions();
        Assert.True(opts.Serialize);
        Assert.Null(opts.Authorize);
    }

    // --- helpers ---

    private static IEnumerable<Type> GetRegisteredWorkflowTypes(TemporalWorkerOptions options) =>
        options.Workflows.Select(wd => wd.Type);
}

// --- test workflow types at namespace level (not nested) to avoid CA1034 ---

[Workflow]
internal sealed class SignalBearingWorkflow : DurableObjectBase
{
    [WorkflowRun]
#pragma warning disable CA1822 // Mark members as static — [WorkflowRun] must be instance
    public Task RunAsync() => DurableObjectRunAsync();
#pragma warning restore CA1822

    [WorkflowSignal]
    public Task SomethingSignaledAsync() => Task.CompletedTask;
}
