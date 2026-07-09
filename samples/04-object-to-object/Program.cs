using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Extensions.Hosting;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.ObjectToObject.Activities;
using TemporalCommunity.DurableObjects.ObjectToObject.Objects;

const string TaskQueue = "object-to-object-demo";

var builder = Host.CreateApplicationBuilder(args);

// Register the Temporal client.
builder.Services.AddTemporalClient(opts =>
    opts.TargetHost = builder.Configuration["Temporal:Address"] ?? "localhost:7233");

// Register IDurableObjectFactory — required for FulfillmentActivities (injected via DI)
// and for the demo runner to create and query objects.
builder.Services.AddDurableObjects(TaskQueue);

// Register the worker.
// AddSingletonActivities registers FulfillmentActivities in DI and wires its [Activity]
// methods to the worker. DI injects IDurableObjectFactory and ILogger automatically.
builder.Services.AddHostedTemporalWorker(TaskQueue)
    .AddDurableObjectWorkflows(Assembly.GetExecutingAssembly())
    .AddSingletonActivities<FulfillmentActivities>();

// Register the demo runner as a hosted service.
builder.Services.AddHostedService<ObjectToObjectDemo>();

await builder.Build().RunAsync();

/// <summary>
/// Background service that drives the object-to-object communication demo.
///
/// Call chain:
///   DemoService -> OrderProcessor.PlaceOrderAsync (workflow update)
///   -> FulfillmentActivities.ReserveInventoryAsync (activity, Temporal-scheduled)
///   -> IDurableObjectFactory.Get -> InventoryTracker.ReserveStockAsync (workflow update)
///
/// Each arrow is a Temporal-durable hop. The full call chain is visible in Temporal Web UI:
///   OrderProcessor workflow history shows ActivityTaskScheduled events
///   InventoryTracker workflow history shows UpdateAccepted events from the activity
/// </summary>
internal sealed class ObjectToObjectDemo : BackgroundService
{
    private readonly IDurableObjectFactory _factory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ObjectToObjectDemo> _logger;

    public ObjectToObjectDemo(
        IDurableObjectFactory factory,
        IHostApplicationLifetime lifetime,
        ILogger<ObjectToObjectDemo> logger)
    {
        _factory = factory;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief startup delay so the worker is ready before we issue updates.
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("=== Object-to-Object Communication Demo ===");
        _logger.LogInformation("Pattern: OrderProcessor -> Activity Bridge -> InventoryTracker");
        _logger.LogInformation("");

        // Step 1: Ensure both objects exist.
        var order = await _factory.GetOrCreateAsync<IOrderProcessor>("order-001", stoppingToken)
            .ConfigureAwait(false);
        var inventory = await _factory.GetOrCreateAsync<IInventoryTracker>("global-inventory", stoppingToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Objects created: OrderProcessor 'order-001', InventoryTracker 'global-inventory'");
        _logger.LogInformation("");

        // Step 2: Place two orders. Each PlaceOrderAsync call:
        //   1. Appends to order history (in the OrderProcessor workflow)
        //   2. Schedules FulfillmentActivities.ReserveInventoryAsync as a Temporal activity
        //   3. That activity calls InventoryTracker.ReserveStockAsync via IDurableObjectFactory
        _logger.LogInformation("Placing order: 5x widget-a ...");
        await order.PlaceOrderAsync("widget-a", 5).ConfigureAwait(false);

        _logger.LogInformation("Placing order: 3x gadget-b ...");
        await order.PlaceOrderAsync("gadget-b", 3).ConfigureAwait(false);

        _logger.LogInformation("");

        // Step 3: Query OrderProcessor history.
        var history = order.GetOrderHistory();
        _logger.LogInformation("Order history ({Count} orders):", history.Count);
        foreach (var entry in history)
        {
            _logger.LogInformation("  - {Entry}", entry);
        }

        _logger.LogInformation("");

        // Step 4: Query InventoryTracker reserved counts.
        // These were updated by the activity bridge — the orders caused the inventory to change.
        var widgetReserved = inventory.GetReservedQuantity("widget-a");
        var gadgetReserved = inventory.GetReservedQuantity("gadget-b");
        _logger.LogInformation("InventoryTracker reserved quantities:");
        _logger.LogInformation("  widget-a: {Count} units reserved", widgetReserved);
        _logger.LogInformation("  gadget-b: {Count} units reserved", gadgetReserved);

        _logger.LogInformation("");
        _logger.LogInformation("=== Demo complete ===");
        _logger.LogInformation("Trace the call chain in Temporal Web UI at http://localhost:8233:");
        _logger.LogInformation("  OrderProcessor 'order-001': workflow history shows 2x ActivityTaskScheduled events");
        _logger.LogInformation("  InventoryTracker 'global-inventory': workflow history shows 2x UpdateAccepted events");
        _logger.LogInformation("The activity bridge makes DO-to-DO calls durable, retryable, and auditable.");

        // Clean up.
        await order.DeactivateAsync().ConfigureAwait(false);
        await inventory.DeactivateAsync().ConfigureAwait(false);

        _lifetime.StopApplication();
    }
}
