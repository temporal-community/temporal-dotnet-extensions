using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Extensions.Hosting;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.InputValidation.Objects;

// ---------------------------------------------------------------------------
// Host setup
// ---------------------------------------------------------------------------

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTemporalClient(opts =>
    opts.TargetHost = builder.Configuration["Temporal:Address"] ?? "localhost:7233");

const string taskQueue = "input-validation-tq";
builder.Services.AddDurableObjects(taskQueue);

// No activities in this sample — BankAccount is pure workflow state.
builder.Services.AddHostedTemporalWorker(taskQueue)
    .AddDurableObjectWorkflows(Assembly.GetExecutingAssembly());

builder.Services.AddHostedService<DemoService>();

await builder.Build().RunAsync();

// ---------------------------------------------------------------------------
// Demo background service
// ---------------------------------------------------------------------------

/// <summary>
/// Demonstrates four input-validation scenarios:
///   1. Happy path: deposit and withdraw.
///   2. Validator rejection: withdraw more than balance.
///   3. Missing object: query an object that was never created.
///   4. Deactivated object: close the account then query it.
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
        await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);

        try
        {
            Console.WriteLine("=== Input Validation: BankAccount Demo ===");
            Console.WriteLine();

            // ------------------------------------------------------------------
            // Scenario 1: Happy path
            // ------------------------------------------------------------------
            Console.WriteLine("--- Scenario 1: Happy path ---");
            var account = await _factory.GetOrCreateAsync<IBankAccount>("acct-001", stoppingToken)
                .ConfigureAwait(false);

            await account.DepositAsync(500m).ConfigureAwait(false);
            Console.WriteLine("Deposited $500.");

            await account.WithdrawAsync(200m).ConfigureAwait(false);
            Console.WriteLine("Withdrew $200.");

            var balance = await _factory.QueryDurableObjectAsync<decimal>(
                "acct-001", "GetBalance", cancellationToken: stoppingToken).ConfigureAwait(false);
            Console.WriteLine($"Balance: {balance:C} (expected $300)");
            Console.WriteLine();

            // ------------------------------------------------------------------
            // Scenario 2: Validator rejection (insufficient funds)
            // ------------------------------------------------------------------
            Console.WriteLine("--- Scenario 2: Validator rejection ---");
            try
            {
                await account.WithdrawAsync(400m).ConfigureAwait(false);
                Console.WriteLine("ERROR: expected exception was not thrown.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Caught expected exception: {ex.GetType().Name}");
                Console.WriteLine($"  Message: {ex.Message}");
            }

            Console.WriteLine();

            // ------------------------------------------------------------------
            // Scenario 3: Missing object
            // Querying a non-existent object throws DurableObjectNotFoundException.
            // ------------------------------------------------------------------
            Console.WriteLine("--- Scenario 3: Missing object ---");
            try
            {
                _ = await _factory.QueryDurableObjectAsync<decimal>(
                    "nonexistent-account", "GetBalance", cancellationToken: stoppingToken)
                    .ConfigureAwait(false);
                Console.WriteLine("ERROR: expected exception was not thrown.");
            }
            catch (DurableObjectNotFoundException ex)
            {
                Console.WriteLine($"Caught {nameof(DurableObjectNotFoundException)}: {ex.Message}");
            }

            Console.WriteLine();

            // ------------------------------------------------------------------
            // Scenario 4: Deactivated object
            // Close the account, then try to query it.
            // ------------------------------------------------------------------
            Console.WriteLine("--- Scenario 4: Deactivated object ---");
            await account.CloseAccountAsync().ConfigureAwait(false);
            Console.WriteLine("Account closed.");

            // Give Temporal visibility a moment to reflect the closed status.
            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);

            try
            {
                _ = await _factory.QueryDurableObjectAsync<decimal>(
                    "acct-001", "GetBalance", cancellationToken: stoppingToken).ConfigureAwait(false);
                Console.WriteLine("ERROR: expected exception was not thrown.");
            }
            catch (DurableObjectNotActiveException ex)
            {
                Console.WriteLine($"Caught {nameof(DurableObjectNotActiveException)}: {ex.Message}");
            }

            Console.WriteLine();
            Console.WriteLine("Demo complete. Press Ctrl+C to exit.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demo failed");
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }
}
