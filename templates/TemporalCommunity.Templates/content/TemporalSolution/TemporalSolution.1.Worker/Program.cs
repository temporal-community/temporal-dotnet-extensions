using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
//#if (UseMinimalApi)
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
//#endif
using Temporalio.Extensions.Hosting;
using GeneratedNamespacePrefix.Shared;
using GeneratedNamespacePrefix.Shared.Activities;
using GeneratedNamespacePrefix.Shared.Workflows;
//#if (OtelWithoutAspire)
using OpenTelemetry;
//#endif
//#if (IncludeOtel)
using Temporalio.Extensions.OpenTelemetry;
//#endif

//#if (UseMinimalApi)
var builder = WebApplication.CreateBuilder(args);
//#else
var builder = Host.CreateApplicationBuilder(args);
//#endif

//#if (IncludeAspire)
builder.AddServiceDefaults();
//#endif

//#if (IncludeOtel)
// OpenTelemetry — the SDK tracing sources, paired with the client-side
// TracingInterceptor registered below.
//#if (OtelWithoutAspire)
// The exporter is gated on OTEL_EXPORTER_OTLP_ENDPOINT, mirroring the same
// convention Aspire's own ServiceDefaults.AddOpenTelemetryExporters() uses, so
// the same config key works whether or not IncludeAspire is later enabled.
//#endif
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(
        TracingInterceptor.ClientSource.Name,
        TracingInterceptor.WorkflowsSource.Name,
        TracingInterceptor.ActivitiesSource.Name,
        TracingInterceptor.NexusSource.Name));

//#if (OtelWithoutAspire)
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry().UseOtlpExporter();
}
//#endif
//#endif

// Resolve shared connection settings and preserve host logging.
// See docs/templates.md for connection precedence.
var connectOptions = SharedTemporalConnection.Resolve(builder.Configuration);
builder.Services.AddTemporalClient(options =>
{
    SharedTemporalConnection.ApplyTo(connectOptions, options);
    //#if (IncludeOtel)
    options.Interceptors = new[] { new TracingInterceptor() };
    //#endif
});

// Worker — the injected-client AddHostedTemporalWorker(taskQueue) overload.
//#if (IncludeOtel)
// The client-side TracingInterceptor registered above carries over into the
// worker automatically so it must not also be added to
// TemporalWorkerOptions.Interceptors to avoid duplicates
//#endif
const string taskQueue = "TemporalSolution.1-tq";
builder.Services.AddHostedTemporalWorker(taskQueue)
    .AddWorkflow<SampleWorkflow>()
    .AddScopedActivities<SampleActivities>();

//#if (UseMinimalApi)
var app = builder.Build();
app.MapGet("/", () => Results.Ok(new { service = "Temporal worker" }));
//#if (IncludeAspire)
app.MapDefaultEndpoints();
//#endif
await app.RunAsync();
//#else
await builder.Build().RunAsync();
//#endif
