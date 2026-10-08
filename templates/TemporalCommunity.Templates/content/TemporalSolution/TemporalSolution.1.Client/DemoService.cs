using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using GeneratedNamespacePrefix.Shared.Workflows;

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

    public DemoService(
        ITemporalClient client,
        ILogger<DemoService> logger,
        IHostApplicationLifetime lifetime)
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
                    taskQueue: taskQueue)
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
}
