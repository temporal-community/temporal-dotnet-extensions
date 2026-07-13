using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects.GeneratedClients.Tests;

[Workflow]
public interface IGeneratedCounter : IDurableObject
{
    [WorkflowUpdate("add-value")]
    Task<int> AddAsync(int amount);

    [WorkflowQuery("read-value")]
    int GetValue();
}
