using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.ObjectToObject.Objects;

/// <summary>
/// Contract for an order processor that places orders and reserves inventory.
/// When an order is placed, the processor bridges to InventoryTracker via an activity —
/// never via a direct workflow-to-workflow call.
/// </summary>
[Workflow]
public interface IOrderProcessor : IDurableObject
{
    /// <summary>
    /// Places an order for the given product and quantity.
    /// Internally calls FulfillmentActivities.ReserveInventoryAsync, which uses IDurableObjectFactory
    /// to update the InventoryTracker.
    /// </summary>
    [WorkflowUpdate]
    Task PlaceOrderAsync(string productId, int quantity);

    /// <summary>Returns the history of placed orders as "{productId}:{quantity}" strings.</summary>
    [WorkflowQuery]
    IReadOnlyList<string> GetOrderHistory();
}
