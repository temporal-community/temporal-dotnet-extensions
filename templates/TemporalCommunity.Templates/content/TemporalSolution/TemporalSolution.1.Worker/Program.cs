using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Temporalio.Extensions.Hosting;
using GeneratedNamespacePrefix.Shared;
using GeneratedNamespacePrefix.Shared.Activities;
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
// ---------------------------------------------------------------------------
// OpenTelemetry — the "Temporalio" ActivitySource, paired with the client-side
// TracingInterceptor registered below.
//#if (OtelWithoutAspire)
// The exporter is gated on OTEL_EXPORTER_OTLP_ENDPOINT, mirroring the same
// convention Aspire's own ServiceDefaults.AddOpenTelemetryExporters() uses, so
// the same config key works whether or not IncludeAspire is later enabled.
//#endif
// ---------------------------------------------------------------------------
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
// Resolve connection options via SharedTemporalConnection.Resolve's three-step
// precedence (environment/profile -> "Temporal:Address" config ->
// localhost:7233), then register ITemporalClient via the SDK's AddTemporalClient.
// ApplyTo copies every resolved setting onto the SDK-managed options, leaving
// the host ILoggerFactory the SDK already assigned in place; the SDK creates a
// lazily-connecting client. See docs/templates.md's "Connecting to Temporal"
// section for the full order.
// ---------------------------------------------------------------------------
var connectOptions = SharedTemporalConnection.Resolve(builder.Configuration);
builder.Services.AddTemporalClient(options =>
{
    SharedTemporalConnection.ApplyTo(connectOptions, options);
//#if (IncludeOtel)
    options.Interceptors = [.. options.Interceptors ?? [], new TracingInterceptor()];
//#endif
});

// ---------------------------------------------------------------------------
// Worker — the injected-client AddHostedTemporalWorker(taskQueue) overload.
//#if (IncludeOtel)
// The client-side TracingInterceptor registered above carries over into the
// worker automatically (the SDK's TemporalWorker adds every client
// interceptor that also implements IWorkerInterceptor), so it must not also
// be added to TemporalWorkerOptions.Interceptors here — doing so would
// double every span.
//#endif
// ---------------------------------------------------------------------------
const string taskQueue = "TemporalSolution.1-tq";
var resolvedTaskQueue = ResolveTaskQueue(builder.Configuration, taskQueue);
builder.Services.AddHostedTemporalWorker(resolvedTaskQueue)
    .AddWorkflow<SampleWorkflow>()
    .AddScopedActivities<SampleActivities>();

await builder.Build().RunAsync();

static string ResolveTaskQueue(IConfiguration configuration, string defaultTaskQueue)
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
