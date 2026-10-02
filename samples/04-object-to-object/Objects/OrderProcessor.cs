#pragma warning disable CA1822 // Workflow methods must be instance methods
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.ObjectToObject.Activities;

namespace TemporalCommunity.DurableObjects.ObjectToObject.Objects;

public sealed record OrderState(
    Collection<string> History, Dictionary<string, string> Statuses, Dictionary<string, Reservation> Requests);

/// <summary>
/// Processes orders and coordinates with InventoryTracker via the activity bridge pattern.
///
/// The key insight: workflow code (PlaceOrderAsync) cannot directly call another DurableObject.
/// Instead it schedules FulfillmentActivities.ReserveInventoryAsync as a Temporal activity.
/// That activity runs outside the workflow scheduler and can safely call IDurableObjectFactory.
///
/// Benefits of the activity bridge:
/// - Retry-safe: the caller supplies a stable order ID, and the inventory object deduplicates it
/// - Auditable: each bridge call appears as an ActivityTaskScheduled event in workflow history
/// - Determinism-safe: the non-deterministic DI call happens inside the activity, not the workflow
/// - Traceable: distributed trace spans cross object boundaries through the activity
/// </summary>
[Workflow]
public sealed class OrderProcessor : DurableObjectBase<OrderState>, IOrderProcessor
{
    [WorkflowInit]
    public OrderProcessor(DurableObjectSnapshot<OrderState>? snapshot = null)
        : base(snapshot, new OrderState([], new(), new())) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<OrderState>? snapshot = null) => DurableObjectRunAsync();

    /// <inheritdoc/>
    [WorkflowUpdate]
    public async Task PlaceOrderAsync(string orderId, string productId, int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        if (State.Requests.TryGetValue(orderId, out var request) && request != new Reservation(productId, quantity))
        {
            throw new InvalidOperationException($"Order ID '{orderId}' was reused with different order data.");
        }

        if (State.Statuses.TryGetValue(orderId, out var existing) && existing == "Fulfilled")
        {
            return;
        }

        if (!State.Statuses.ContainsKey(orderId))
        {
            State.Requests.Add(orderId, new Reservation(productId, quantity));
            State.History.Add($"{orderId}:{productId}:{quantity}");
        }

        State.Statuses[orderId] = "Pending";

        Workflow.Logger.LogInformation(
            "OrderProcessor: placing order for {Quantity}x '{ProductId}'", quantity, productId);

        // Cross into the activity boundary. FulfillmentActivities uses IDurableObjectFactory
        // from DI to update the InventoryTracker — this is the only safe way to call another
        // DurableObject from workflow code in v1.
        try
        {
            await ExecuteActivityAsync(
                (FulfillmentActivities act) => act.ReserveInventoryAsync(orderId, productId, quantity),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
            State.Statuses[orderId] = "Fulfilled";
        }
        catch
        {
            // The reservation may have succeeded even if its response was lost. The target's
            // operation-ID deduplication makes a later retry safe; retain an explicit failure state.
            State.Statuses[orderId] = "Failed";
            throw;
        }

        Workflow.Logger.LogInformation(
            "OrderProcessor: order fulfilled for {Quantity}x '{ProductId}'", quantity, productId);
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public IReadOnlyList<string> GetOrderHistory() => State.History;

    [WorkflowQuery]
    public string? GetOrderStatus(string orderId) =>
        State.Statuses.GetValueOrDefault(orderId);
}
