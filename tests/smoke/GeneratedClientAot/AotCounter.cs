using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects.AotSmoke;

[Workflow]
public interface IAotCounter : IDurableObject
{
    [WorkflowUpdate]
    Task IncrementAsync();

    [WorkflowQuery]
    int GetCount();
}
