using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using GeneratedNamespacePrefix.Shared;
using GeneratedNamespacePrefix.Shared.Workflows;
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
// pointed at different servers. See docs/templates.md's "Connecting to
// Temporal" section for the full order. Registration goes through the SDK's
// AddTemporalClient, exactly as in Worker; ApplyTo copies every resolved
// setting while keeping the host ILoggerFactory the SDK already assigned.
// ---------------------------------------------------------------------------
var connectOptions = SharedTemporalConnection.Resolve(builder.Configuration);
builder.Services.AddTemporalClient(options =>
{
    SharedTemporalConnection.ApplyTo(connectOptions, options);
//#if (IncludeOtel)
    options.Interceptors = [.. options.Interceptors ?? [], new TracingInterceptor()];
//#endif
});

builder.Services.AddHostedService<DemoService>();

await builder.Build().RunAsync();

/// <summary>
/// Starts <see cref="SampleWorkflow"/>, waits for its result, prints it, then stops the host — a
/// one-shot demo, not a long-running service. Replace with real client logic.
/// </summary>
internal sealed class DemoService : BackgroundService
{
    private const string taskQueue = "TemporalSolution.1-tq";
    private readonly ITemporalClient _client;
    private readonly ILogger<DemoService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly string _taskQueue;

    public DemoService(
        ITemporalClient client,
        ILogger<DemoService> logger,
        IHostApplicationLifetime lifetime,
        IConfiguration configuration)
    {
        _client = client;
        _logger = logger;
        _lifetime = lifetime;
        _taskQueue = ResolveTaskQueue(configuration, taskQueue);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var handle = await _client.StartWorkflowAsync(
                (SampleWorkflow workflow) => workflow.RunAsync("world"),
                new WorkflowOptions(
                    id: $"temporal-solution-{Guid.NewGuid():N}",
                    taskQueue: _taskQueue)
                {
                    Rpc = new RpcOptions { CancellationToken = stoppingToken },
                });

            var result = await handle.GetResultAsync(
                rpcOptions: new RpcOptions { CancellationToken = stoppingToken });
            _logger.LogInformation("Workflow result: {Result}", result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Demo workflow stopped because the host is shutting down.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demo workflow failed");
            Environment.ExitCode = 1;
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }

    private static string ResolveTaskQueue(IConfiguration configuration, string defaultTaskQueue)
    {
        var configured = configuration["Temporal:TaskQueue"];
        if (configured is null)
        {
            return defaultTaskQueue;
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "Configuration value 'Temporal:TaskQueue' must not be blank. Set it to a valid task queue name or remove it.");
        }

        return configured;
    }
}
