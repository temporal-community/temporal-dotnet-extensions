using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.ObjectToObject.Objects;

namespace TemporalCommunity.DurableObjects.ObjectToObject.Activities;

/// <summary>
/// The activity bridge between OrderProcessor and InventoryTracker.
///
/// This is the core of the DO-to-DO communication pattern:
///   OrderProcessor (workflow) -> ExecuteActivityAsync -> FulfillmentActivities (activity)
///   -> IDurableObjectFactory.Get -> InventoryTracker (workflow)
///
/// Activities run outside the workflow scheduler, so they CAN:
/// - Use IDurableObjectFactory from DI (non-deterministic RPC)
/// - Use ILogger for real logging
/// - Do arbitrary I/O
///
/// The activity is the non-deterministic boundary. Temporal records the activity
/// result in workflow history, making replays deterministic even though the
/// underlying call went out over the network to another workflow.
/// </summary>
public sealed class FulfillmentActivities
{
    private readonly IDurableObjectFactory _factory;
    private readonly ILogger<FulfillmentActivities> _logger;

    /// <summary>
    /// DI constructor — IDurableObjectFactory and ILogger are injected by the hosting infrastructure.
    /// IDurableObjectFactory is registered by AddDurableObjects() in Program.cs.
    /// </summary>
    public FulfillmentActivities(IDurableObjectFactory factory, ILogger<FulfillmentActivities> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>
    /// Reserves inventory in the global InventoryTracker by calling it via IDurableObjectFactory.
    /// This is safe here because activities run outside the workflow determinism constraint.
    /// </summary>
    [Activity]
    public async Task ReserveInventoryAsync(string productId, int quantity)
    {
        _logger.LogInformation(
            "[Activity] Reserving {Quantity} units of '{ProductId}' in global inventory", quantity, productId);

        // Get a proxy for the canonical global inventory tracker.
        // Get() does not issue an RPC — the proxy is a local dispatch facade.
        // The actual RPC happens when we call ReserveStockAsync below.
        var inventory = _factory.Get<IInventoryTracker>("global-inventory");
        await inventory.ReserveStockAsync(productId, quantity).ConfigureAwait(false);

        _logger.LogInformation(
            "[Activity] Reservation complete for {Quantity}x '{ProductId}'", quantity, productId);
    }
}
