using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.ObjectToObject.Objects;

/// <summary>
/// Contract for a global inventory tracker.
/// Called indirectly by OrderProcessor via an activity bridge — never called directly
/// from workflow code (direct DO-to-DO calls are not supported in v1).
/// </summary>
[Workflow]
public interface IInventoryTracker : IDurableObject
{
    /// <summary>
    /// Reserves the requested quantity of a product once per operation ID. Repeating the same
    /// operation ID and arguments is a no-op; reusing an ID with different arguments fails.
    /// Called by FulfillmentActivities.ReserveInventoryAsync — the activity is the bridge.
    /// </summary>
    [WorkflowUpdate]
    Task ReserveStockAsync(string operationId, string productId, int quantity);

    /// <summary>
    /// Returns how many units of the given product are currently reserved (read-only, no await).
    /// </summary>
    [WorkflowQuery]
    int GetReservedQuantity(string productId);
}
