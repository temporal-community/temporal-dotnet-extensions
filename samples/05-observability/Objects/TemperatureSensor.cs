#pragma warning disable CA1822 // Workflow methods must be instance methods
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.Observability.Activities;

namespace TemporalCommunity.DurableObjects.Observability.Objects;

/// <summary>
/// IoT temperature sensor DurableObject.
/// Demonstrates OpenTelemetry integration: each activity call produces a child span inside
/// the workflow task span, visible in trace output.
/// </summary>
[Workflow]
public sealed class TemperatureSensor : DurableObjectBase, ITemperatureSensor
{
    private double _latestReading;
    private int _readingCount;

    /// <summary>Required boilerplate — [WorkflowRun] is not inherited.</summary>
    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    /// <inheritdoc/>
    protected override Task OnActivateAsync()
    {
        Workflow.Logger.LogInformation(
            "TemperatureSensor {Id} activated", WorkflowId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    [WorkflowUpdate]
    public async Task RecordReadingAsync(double celsius)
    {
        _latestReading = celsius;
        _readingCount++;

        Workflow.Logger.LogInformation(
            "Recording reading #{Count}: {Celsius}°C", _readingCount, celsius);

        // The activity call produces a child OTel span. In Jaeger you will see:
        //   workflow-task
        //     └─ update:RecordReadingAsync
        //          └─ activity:PersistReadingAsync
        await ExecuteActivityAsync(
            (SensorActivities act) => act.PersistReadingAsync(WorkflowId, celsius),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public double GetLatestReading() => _latestReading;

    /// <inheritdoc/>
    [WorkflowQuery]
    public int GetReadingCount() => _readingCount;
}
