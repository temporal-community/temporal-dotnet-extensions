using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using GeneratedClassNamePrefix.Shared;
using GeneratedClassNamePrefix.Shared.Workflows;
//#if (IncludeOtel)
using OpenTelemetry;
using OpenTelemetry.Trace;
using Temporalio.Extensions.OpenTelemetry;
//#endif

var builder = Host.CreateApplicationBuilder(args);

//#if (IncludeAspire)
builder.AddServiceDefaults();
//#endif

//#if (IncludeOtel)
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("Temporalio"));

//#if (OtelWithoutAspire)
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry().UseOtlpExporter();
}
//#endif
//#endif

// ---------------------------------------------------------------------------
// Resolve connection options via SharedTemporalConnection.Resolve — the exact
// same three-step precedence Worker uses, so Client and Worker never end up
// pointed at different servers. See docs/TEMPLATES.md's "Connecting to
// Temporal" section for the full order.
// ---------------------------------------------------------------------------
var connectOptions = SharedTemporalConnection.Resolve(builder.Configuration);
builder.Services.AddSingleton<ITemporalClient>(provider =>
{
    var options = (TemporalClientConnectOptions)connectOptions.Clone();
    options.LoggerFactory = provider.GetRequiredService<ILoggerFactory>();
//#if (IncludeOtel)
    options.Interceptors = new[] { new TracingInterceptor() };
//#endif
    return TemporalClient.CreateLazy(options);
});

builder.Services.AddHostedService<DemoService>();

await builder.Build().RunAsync().ConfigureAwait(false);

/// <summary>
/// Starts <see cref="SampleWorkflow"/>, waits for its result, prints it, then stops the host — a
/// one-shot demo, not a long-running service. Replace with real client logic.
/// </summary>
internal sealed class DemoService : BackgroundService
{
    private readonly ITemporalClient _client;
    private readonly ILogger<DemoService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public DemoService(ITemporalClient client, ILogger<DemoService> logger, IHostApplicationLifetime lifetime)
    {
        _client = client;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var handle = await _client.StartWorkflowAsync(
                (SampleWorkflow workflow) => workflow.RunAsync("world"),
                new WorkflowOptions(
                    id: $"temporal-solution-{Guid.NewGuid():N}",
                    taskQueue: "temporal-solution-tq")).ConfigureAwait(false);

            var result = await handle.GetResultAsync().ConfigureAwait(false);
            _logger.LogInformation("Workflow result: {Result}", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demo workflow failed");
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }
}
