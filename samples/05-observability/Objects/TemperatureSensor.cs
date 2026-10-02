#pragma warning disable CA1822 // Workflow methods must be instance methods
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.Observability.Activities;

namespace TemporalCommunity.DurableObjects.Observability.Objects;

public sealed record SensorState(double LatestReading, int ReadingCount);

/// <summary>
/// IoT temperature sensor DurableObject.
/// Demonstrates OpenTelemetry integration: each activity call produces a child span inside
/// the workflow task span, visible in trace output.
/// </summary>
[Workflow]
public sealed class TemperatureSensor : DurableObjectBase<SensorState>, ITemperatureSensor
{
    [WorkflowInit]
    public TemperatureSensor(DurableObjectSnapshot<SensorState>? snapshot = null)
        : base(snapshot, new SensorState(0, 0)) { }

    /// <summary>Required boilerplate — [WorkflowRun] is not inherited.</summary>
    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<SensorState>? snapshot = null) => DurableObjectRunAsync();

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
        // The activity call produces a child OTel span. In Jaeger you will see:
        //   workflow-task
        //     └─ update:RecordReadingAsync
        //          └─ activity:PersistReadingAsync
        await ExecuteActivityAsync(
            (SensorActivities act) => act.PersistReadingAsync(WorkflowId, celsius),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        State = new SensorState(celsius, State.ReadingCount + 1);
        Workflow.Logger.LogInformation(
            "Recorded reading #{Count}: {Celsius}°C", State.ReadingCount, celsius);
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public double GetLatestReading() => State.LatestReading;

    /// <inheritdoc/>
    [WorkflowQuery]
    public int GetReadingCount() => State.ReadingCount;
}
