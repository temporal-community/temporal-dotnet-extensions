using Microsoft.Extensions.Logging;
using Temporalio.Activities;

namespace TemporalCommunity.DurableObjects.Observability.Activities;

/// <summary>
/// Activities for the TemperatureSensor demo.
/// Each [Activity] method produces an OpenTelemetry span when the TracingInterceptor is active.
/// </summary>
public sealed class SensorActivities
{
    private readonly ILogger<SensorActivities> _logger;

    /// <summary>DI constructor — ILogger injected by the hosting infrastructure.</summary>
    public SensorActivities(ILogger<SensorActivities> logger) => _logger = logger;

    /// <summary>
    /// Persists a sensor reading. In production this would write to a time-series database.
    /// The activity runs outside the workflow scheduler, so regular async I/O is safe.
    /// </summary>
    [Activity]
    public Task PersistReadingAsync(string sensorId, double celsius)
    {
        _logger.LogInformation(
            "[Activity] Persisted reading for sensor {SensorId}: {Celsius}°C",
            sensorId, celsius);
        return Task.CompletedTask;
    }
}
