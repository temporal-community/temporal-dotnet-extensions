using Microsoft.Extensions.Logging;
using Temporalio.Activities;

namespace TemporalCommunity.DurableObjects.Scheduling.Activities;

/// <summary>
/// Activities for the scheduling demo.
/// Activities CAN do I/O, use ILogger, and access DI — they run outside the workflow scheduler.
/// </summary>
public sealed class SchedulingActivities
{
    private readonly ILogger<SchedulingActivities> _logger;

    /// <summary>DI constructor — ILogger is injected by the hosting infrastructure.</summary>
    public SchedulingActivities(ILogger<SchedulingActivities> logger) => _logger = logger;

    /// <summary>
    /// Simulates publishing a report. In a real app this would write to storage, call an API, etc.
    /// </summary>
    [Activity]
    public Task PublishReportAsync(string reportId)
    {
        _logger.LogInformation("[Activity] Published report {ReportId}", reportId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Simulates notifying a subscriber. deliveryId can be used as an idempotency key when
    /// calling downstream systems that honor idempotency keys. This demo only logs a simulated send.
    /// </summary>
    [Activity]
    public Task NotifySubscriberAsync(string email, string deliveryId)
    {
        _logger.LogInformation(
            "[Activity] Notified {Email} (deliveryId={DeliveryId})", email, deliveryId);
        return Task.CompletedTask;
    }
}
