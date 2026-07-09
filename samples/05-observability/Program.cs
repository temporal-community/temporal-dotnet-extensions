using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Temporalio.Extensions.Hosting;
using Temporalio.Extensions.OpenTelemetry;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.Observability.Activities;
using TemporalCommunity.DurableObjects.Observability.Objects;

// ---------------------------------------------------------------------------
// Host setup
// ---------------------------------------------------------------------------

var builder = Host.CreateApplicationBuilder(args);

// ---------------------------------------------------------------------------
// OpenTelemetry — add tracing with Temporal source and console exporter.
// In production replace AddConsoleExporter() with an OTLP exporter pointed
// at Jaeger, Honeycomb, Grafana Tempo, or another backend.
// ---------------------------------------------------------------------------
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Temporalio")
        .AddConsoleExporter());

// ---------------------------------------------------------------------------
// Temporal client — attach the TracingInterceptor so client-side RPC calls
// (StartWorkflow, ExecuteUpdate, etc.) are wrapped in OTel spans.
// ---------------------------------------------------------------------------
builder.Services.AddTemporalClient(opts =>
{
    opts.TargetHost = builder.Configuration["Temporal:Address"] ?? "localhost:7233";
    opts.Interceptors = [new TracingInterceptor()];
});

// ---------------------------------------------------------------------------
// DurableObject factory (client-side proxy).
// ---------------------------------------------------------------------------
const string taskQueue = "observability-tq";
builder.Services.AddDurableObjects(taskQueue);

// ---------------------------------------------------------------------------
// Worker — attach the TracingInterceptor to the worker as well so workflow
// tasks and activity invocations produce spans on the server side.
// AddDurableObjectWorkflows adds DurableObjectWorkerInterceptor automatically;
// ConfigureOptions prepends the TracingInterceptor before it runs.
// ---------------------------------------------------------------------------
builder.Services.AddHostedTemporalWorker(taskQueue)
    .ConfigureOptions(opts => opts.Interceptors = [new TracingInterceptor()])
    .AddDurableObjectWorkflows(Assembly.GetExecutingAssembly())
    .AddSingletonActivities<SensorActivities>();

builder.Services.AddHostedService<DemoService>();

await builder.Build().RunAsync();

// ---------------------------------------------------------------------------
// Demo background service
// ---------------------------------------------------------------------------

/// <summary>
/// Records three temperature readings for a single sensor, then queries the count.
/// Watch the console for OTel span output from the ConsoleExporter.
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
            Console.WriteLine("=== Observability: TemperatureSensor Demo ===");
            Console.WriteLine("OTel spans will appear below — look for 'StartWorkflow',");
            Console.WriteLine("'ExecuteUpdate', and 'RunActivity' span names.");
            Console.WriteLine();

            var sensor = await _factory.GetOrCreateAsync<ITemperatureSensor>(
                "sensor-kitchen", stoppingToken).ConfigureAwait(false);

            double[] readings = [21.5, 22.1, 23.0];
            foreach (var temp in readings)
            {
                await sensor.RecordReadingAsync(temp).ConfigureAwait(false);
                Console.WriteLine($"Recorded {temp}°C");
            }

            var count = await _factory.QueryDurableObjectAsync<int>(
                "sensor-kitchen", "GetReadingCount", cancellationToken: stoppingToken)
                .ConfigureAwait(false);

            var latest = await _factory.QueryDurableObjectAsync<double>(
                "sensor-kitchen", "GetLatestReading", cancellationToken: stoppingToken)
                .ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine($"Total readings: {count} (expected 3)");
            Console.WriteLine($"Latest reading: {latest}°C (expected 23.0)");
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
