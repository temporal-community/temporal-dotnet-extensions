#pragma warning disable CA1822 // Workflow methods must be instance methods
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.ObjectToObject.Activities;

namespace TemporalCommunity.DurableObjects.ObjectToObject.Objects;

/// <summary>
/// Processes orders and coordinates with InventoryTracker via the activity bridge pattern.
///
/// The key insight: workflow code (PlaceOrderAsync) cannot directly call another DurableObject.
/// Instead it schedules FulfillmentActivities.ReserveInventoryAsync as a Temporal activity.
/// That activity runs outside the workflow scheduler and can safely call IDurableObjectFactory.
///
/// Benefits of the activity bridge:
/// - Durable: if the activity fails, Temporal retries it automatically
/// - Auditable: each bridge call appears as an ActivityTaskScheduled event in workflow history
/// - Determinism-safe: the non-deterministic DI call happens inside the activity, not the workflow
/// - Traceable: distributed trace spans cross object boundaries through the activity
/// </summary>
[Workflow]
public sealed class OrderProcessor : DurableObjectBase, IOrderProcessor
{
    private readonly List<string> _orderHistory = [];

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    /// <inheritdoc/>
    [WorkflowUpdate]
    public async Task PlaceOrderAsync(string productId, int quantity)
    {
        _orderHistory.Add($"{productId}:{quantity}");

        Workflow.Logger.LogInformation(
            "OrderProcessor: placing order for {Quantity}x '{ProductId}'", quantity, productId);

        // Cross into the activity boundary. FulfillmentActivities uses IDurableObjectFactory
        // from DI to update the InventoryTracker — this is the only safe way to call another
        // DurableObject from workflow code in v1.
        await ExecuteActivityAsync(
            (FulfillmentActivities act) => act.ReserveInventoryAsync(productId, quantity),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });

        Workflow.Logger.LogInformation(
            "OrderProcessor: order fulfilled for {Quantity}x '{ProductId}'", quantity, productId);
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public IReadOnlyList<string> GetOrderHistory() => _orderHistory;
}
