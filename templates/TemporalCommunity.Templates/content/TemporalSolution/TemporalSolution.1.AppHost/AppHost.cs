using TemporalCommunity.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Provisions a local Temporal dev server automatically (via Temporalio.Testing.WorkflowEnvironment
// under the hood — no separately installed Temporal CLI required) as part of `aspire run`. See
// docs/templates.md's "Multi-project templates" section for the IncludeAspire=false alternative
// (run your own `temporal server start-dev`).
var temporal = builder.AddTemporalLocalDevServer("temporal");

var worker = builder.AddProject<Projects.GeneratedAspirePrefix_Worker>("worker")
    .WaitFor(temporal)
    .WithReference(temporal);

builder.AddProject<Projects.GeneratedAspirePrefix_Client>("client")
    .WaitFor(temporal)
    .WithReference(temporal)
    .WaitFor(worker);

await builder.Build().RunAsync();
