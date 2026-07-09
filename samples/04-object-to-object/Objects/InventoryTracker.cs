#pragma warning disable CA1822 // Workflow methods must be instance methods
#pragma warning disable CA2007 // ConfigureAwait — workflow code must use ConfigureAwait(true), never false
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.ObjectToObject.Objects;

/// <summary>
/// Tracks reserved inventory quantities per product.
/// This object is the TARGET of the activity bridge pattern: it receives updates from
/// FulfillmentActivities, which is called by OrderProcessor's workflow code.
///
/// Direct workflow-to-workflow calls are not supported in v1 (determinism + fan-out concerns).
/// The activity acts as the non-deterministic boundary that safely crosses object boundaries.
/// </summary>
[Workflow]
public sealed class InventoryTracker : DurableObjectBase, IInventoryTracker
{
    // productId -> total reserved quantity
    private readonly Dictionary<string, int> _reserved = new();

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    /// <inheritdoc/>
    [WorkflowUpdate]
    public Task ReserveStockAsync(string productId, int quantity)
    {
        _reserved[productId] = _reserved.GetValueOrDefault(productId, 0) + quantity;
        Workflow.Logger.LogInformation(
            "InventoryTracker: reserved {Quantity} units of '{ProductId}' (total reserved: {Total})",
            quantity, productId, _reserved[productId]);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public int GetReservedQuantity(string productId) =>
        _reserved.GetValueOrDefault(productId, 0);
}
