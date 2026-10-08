using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Temporalio.Client;
using GeneratedNamespacePrefix.Shared;
//#if (OtelWithoutAspire)
using OpenTelemetry;
//#endif
//#if (IncludeOtel)
using Temporalio.Extensions.OpenTelemetry;
//#endif

var builder = Host.CreateApplicationBuilder(args);

//#if (IncludeAspire)
builder.AddServiceDefaults();
//#endif

//#if (IncludeOtel)
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

builder.Services.AddHostedService<DemoService>();

await builder.Build().RunAsync();
