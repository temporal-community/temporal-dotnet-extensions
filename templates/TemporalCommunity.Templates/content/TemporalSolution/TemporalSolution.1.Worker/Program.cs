using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using Temporalio.Extensions.Hosting;
using GeneratedClassNamePrefix.Shared;
using GeneratedClassNamePrefix.Shared.Activities;
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
// localhost:7233) and register a lazily-connecting ITemporalClient. See
// docs/TEMPLATES.md's "Connecting to Temporal" section for the full order.
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
const string taskQueue = "temporal-solution-tq";
builder.Services.AddHostedTemporalWorker(taskQueue)
    .AddWorkflow<SampleWorkflow>()
    .AddScopedActivities<SampleActivities>();

await builder.Build().RunAsync().ConfigureAwait(false);
