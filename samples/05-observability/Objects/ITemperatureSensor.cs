using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.Observability.Objects;

/// <summary>
/// Contract for an IoT temperature sensor DurableObject.
/// Each update invocation produces an OpenTelemetry span via the TracingInterceptor.
/// </summary>
[Workflow]
public interface ITemperatureSensor : IDurableObject
{
    /// <summary>Records a temperature reading. Calls an activity to persist it.</summary>
    [WorkflowUpdate]
    Task RecordReadingAsync(double celsius);

    /// <summary>Returns the most recent reading in Celsius.</summary>
    [WorkflowQuery]
    double GetLatestReading();

    /// <summary>Returns the total number of readings recorded.</summary>
    [WorkflowQuery]
    int GetReadingCount();
}
