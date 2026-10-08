using TemporalCommunity.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Start a local Temporal dev server.
// Alternatives: AddTemporalCliServer (CLI), AddTemporalDevContainer (Docker),
// or AddTemporalCloud (Temporal Cloud).
var temporal = builder.AddTemporalLocalDevServer("temporal");

var worker = builder.AddProject<Projects.GeneratedAspirePrefix_Worker>("worker")
    .WaitFor(temporal)
    .WithReference(temporal);
//#if (UseMinimalApi)
worker.WithHttpHealthCheck("/health");
//#endif

builder.AddProject<Projects.GeneratedAspirePrefix_Client>("client")
    .WaitFor(temporal)
    .WithReference(temporal)
    .WaitFor(worker);

await builder.Build().RunAsync();
