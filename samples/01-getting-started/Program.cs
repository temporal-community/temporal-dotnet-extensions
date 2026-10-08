using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Extensions.Hosting;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.GettingStarted.Activities;
using TemporalCommunity.DurableObjects.GettingStarted.Objects;

// ---------------------------------------------------------------------------
// Host setup
// ---------------------------------------------------------------------------

var builder = Host.CreateApplicationBuilder(args);

// Register a Temporal client pointed at the local server (or override via appsettings).
builder.Services.AddTemporalClient(opts =>
    opts.TargetHost = builder.Configuration["Temporal:Address"] ?? "localhost:7233");

// Register the IDurableObjectFactory singleton (client-side proxy factory).
// The task-queue name must match what the worker polls.
const string taskQueue = "getting-started-tq";
builder.Services.AddDurableObjects(taskQueue);

// Register the hosted worker that runs DurableObject workflows and activities.
builder.Services.AddHostedTemporalWorker(taskQueue)
    .AddDurableObjectWorkflows(Assembly.GetExecutingAssembly())
    .AddSingletonActivities<PageCounterActivities>();

// Register the demo background service.
builder.Services.AddHostedService<DemoService>();

await builder.Build().RunAsync();

// ---------------------------------------------------------------------------
// Demo background service
// ---------------------------------------------------------------------------

/// <summary>
/// Drives the getting-started demo scenario:
///   1. Obtain a generated client (no RPC).
///   2. Increment directly, materializing the object through Update-with-Start if needed.
///   3. Query the count via the generated async method.
///   4. Increment twice more and verify the change.
///   5. List all active counters.
/// </summary>
internal sealed class DemoService : BackgroundService
{
    private readonly IDurableObjectFactory _factory;
    private readonly ILogger<DemoService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public DemoService(
        IDurableObjectFactory factory,
        ILogger<DemoService> logger,
        IHostApplicationLifetime lifetime)
    {
        _factory = factory;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief grace period so the worker is registered before we send updates.
        await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);

        try
        {
            Console.WriteLine("=== Getting Started: PageCounter Demo ===");
            Console.WriteLine();

            // Step 1: Get the source-generated concrete client — no RPC issued here.
            // It avoids DispatchProxy and adds asynchronous query methods.
            var client = _factory.GetPageCounterClient(
                "home",
                new DurableObjectCallOptions(cancellationToken: stoppingToken));
            Console.WriteLine($"Got generated client for object id 'home' (no RPC yet).");

            // Step 2: Send the first update directly. Generated update methods use
            // Update-with-Start, so this materializes a missing object without a separate start.
            await client.IncrementAsync().ConfigureAwait(false);
            Console.WriteLine("  Increment 1 complete.");

            // Step 3: The update guarantees an execution now exists, so query through the
            // generated asynchronous method without blocking a service thread.
            var countAfterFirstIncrement = await client.GetCountAsync().ConfigureAwait(false);

            // Step 4: Send two more updates.
            for (var i = 2; i <= 3; i++)
            {
                await client.IncrementAsync().ConfigureAwait(false);
                Console.WriteLine($"  Increment {i} complete.");
            }

            Console.WriteLine();

            var count = await client.GetCountAsync().ConfigureAwait(false);
            if (count != countAfterFirstIncrement + 2) throw new InvalidOperationException("The counter did not retain all three updates.");
            Console.WriteLine($"Current view count for 'home': {count}");
            Console.WriteLine();

            // Step 5: List all active PageCounter executions.
            Console.WriteLine("Active PageCounter objects:");
            await foreach (var execution in _factory.ListDurableObjectExecutionsAsync<IPageCounter>(
                cancellationToken: stoppingToken).ConfigureAwait(false))
            {
                Console.WriteLine(
                    $"  - {execution.ObjectId} ({execution.Status}, run {execution.RunId})");
            }

            Console.WriteLine();
            Console.WriteLine("Demo complete. The host will stop; the counter remains open.");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            _logger.LogError(ex, "Demo failed");
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }
}
