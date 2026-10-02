#pragma warning disable CA1822 // Workflow methods must be instance methods
#pragma warning disable CA2007 // ConfigureAwait — workflow code must use ConfigureAwait(true), never false
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.ObjectToObject.Objects;

public sealed record Reservation(string ProductId, int Quantity);
public sealed record InventoryState(
    Dictionary<string, int> Reserved, Dictionary<string, Reservation> Reservations);

/// <summary>
/// Tracks reserved inventory quantities per product.
/// This object is the TARGET of the activity bridge pattern: it receives updates from
/// FulfillmentActivities, which is called by OrderProcessor's workflow code.
///
/// Direct workflow-to-workflow calls are not supported in v1 (determinism + fan-out concerns).
/// The activity acts as the non-deterministic boundary that safely crosses object boundaries.
/// </summary>
[Workflow]
public sealed class InventoryTracker : DurableObjectBase<InventoryState>, IInventoryTracker
{
    [WorkflowInit]
    public InventoryTracker(DurableObjectSnapshot<InventoryState>? snapshot = null)
        : base(snapshot, new InventoryState(new(), new())) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<InventoryState>? snapshot = null) => DurableObjectRunAsync();

    /// <inheritdoc/>
    [WorkflowUpdate]
    public Task ReserveStockAsync(string operationId, string productId, int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        if (State.Reservations.TryGetValue(operationId, out var existing))
        {
            if (existing != new Reservation(productId, quantity))
            {
                throw new InvalidOperationException(
                    $"Operation ID '{operationId}' was already used for a different reservation.");
            }

            return Task.CompletedTask;
        }

        // Carry the operation key alongside the totals so retries remain deduplicated after rollover.
        State.Reservations.Add(operationId, new Reservation(productId, quantity));
        State.Reserved[productId] = State.Reserved.GetValueOrDefault(productId, 0) + quantity;
        Workflow.Logger.LogInformation(
            "InventoryTracker: reserved {Quantity} units of '{ProductId}' (total reserved: {Total})",
            quantity, productId, State.Reserved[productId]);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public int GetReservedQuantity(string productId) =>
        State.Reserved.GetValueOrDefault(productId, 0);
}
