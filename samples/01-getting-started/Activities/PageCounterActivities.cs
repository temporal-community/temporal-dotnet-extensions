using Microsoft.Extensions.Logging;
using Temporalio.Activities;

namespace TemporalCommunity.DurableObjects.GettingStarted.Activities;

/// <summary>
/// Activity class for the PageCounter demo.
/// Activities CAN do I/O, use ILogger, and access DI — they run outside the workflow scheduler.
/// </summary>
public sealed class PageCounterActivities
{
    private readonly ILogger<PageCounterActivities> _logger;

    /// <summary>DI constructor — ILogger is injected by the hosting infrastructure.</summary>
    public PageCounterActivities(ILogger<PageCounterActivities> logger) => _logger = logger;

    /// <summary>
    /// Records a page view. In a real app this would write to a database or analytics service.
    /// </summary>
    [Activity]
    public Task RecordViewAsync(string slug, int newCount)
    {
        _logger.LogInformation("[Activity] Recorded view for /{Slug}: count={Count}", slug, newCount);
        return Task.CompletedTask;
    }
}
