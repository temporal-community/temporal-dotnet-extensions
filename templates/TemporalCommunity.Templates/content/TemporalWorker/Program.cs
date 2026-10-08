using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Temporalio.Extensions.Hosting;
using TemporalWorker1;
using TemporalWorker1.Activities;
using TemporalWorker1.Workflows;
//#if (IncludeOtel)
using OpenTelemetry;
using OpenTelemetry.Trace;
using Temporalio.Extensions.OpenTelemetry;
//#endif

var builder = Host.CreateApplicationBuilder(args);

//#if (IncludeOtel)
// Trace Temporal spans; export them when an OTLP endpoint is configured.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(
        TracingInterceptor.ClientSource.Name,
        TracingInterceptor.WorkflowsSource.Name,
        TracingInterceptor.ActivitiesSource.Name,
        TracingInterceptor.NexusSource.Name));

if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry().UseOtlpExporter();
}
//#endif

// Resolve the connection once and apply it to the SDK-managed client.
// See docs/templates.md for connection precedence.
var connectOptions = TemporalWorker1.TemporalWorkerConnection.Resolve(builder.Configuration);
builder.Services.AddTemporalClient(options =>
{
    TemporalWorker1.TemporalWorkerConnection.ApplyTo(connectOptions, options);
//#if (IncludeOtel)
    options.Interceptors = new[] { new TracingInterceptor() };
//#endif
});

//#if (IncludeOtel)
// The client's tracing interceptor also runs on the worker; do not register it twice.
//#endif
const string taskQueue = "TemporalWorker1-tq";
builder.Services.AddHostedTemporalWorker(taskQueue)
    .AddWorkflow<SampleWorkflow>()
    .AddScopedActivities<SampleActivities>();

await builder.Build().RunAsync();
