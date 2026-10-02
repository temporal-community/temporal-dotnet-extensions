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
    /// Places or retries an order. The caller must reuse the same stable order ID for retries.
    /// Internally calls FulfillmentActivities.ReserveInventoryAsync, which uses IDurableObjectFactory
    /// to update the InventoryTracker.
    /// </summary>
    [WorkflowUpdate]
    Task PlaceOrderAsync(string orderId, string productId, int quantity);

    /// <summary>Returns the history of placed orders as "{productId}:{quantity}" strings.</summary>
    [WorkflowQuery]
    IReadOnlyList<string> GetOrderHistory();

    /// <summary>Returns Pending, Fulfilled, or Failed for an order ID, or null if unknown.</summary>
    [WorkflowQuery]
    string? GetOrderStatus(string orderId);
}
