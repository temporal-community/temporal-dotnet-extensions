using Temporalio.Converters;
using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Objects;

[Workflow]
public class RuntimeSignalGuardObject : DurableObjectBase
{
    private int _count;

    [WorkflowRun]
    public virtual Task RunAsync() => DurableObjectRunAsync();

    [WorkflowSignal]
    public Task NamedAsync()
    {
        _count++;
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public int ReadCount() => _count;

    [WorkflowUpdate]
    public Task InstallDynamicAsync()
    {
        Workflow.DynamicSignal = WorkflowSignalDefinition.CreateWithoutAttribute(
            null, (Func<string, IRawValue[], Task>)CatchAllAsync);
        return Task.CompletedTask;
    }

    protected Task CatchAllAsync(string name, IRawValue[] args)
    {
        _count += 100;
        return Task.CompletedTask;
    }
}

[Workflow]
public sealed class DeclaredDynamicSignalGuardObject : RuntimeSignalGuardObject
{
    [WorkflowRun]
    public override Task RunAsync() => base.RunAsync();

#pragma warning disable DO0001 // Deliberately unsupported definition tests manual/late registration bypass.
    [WorkflowSignal(Dynamic = true)]
    public Task DynamicAsync(string name, IRawValue[] args) => CatchAllAsync(name, args);
#pragma warning restore DO0001
}
