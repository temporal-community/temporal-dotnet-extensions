using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.Scheduling.Objects;

/// <summary>
/// Contract for a canonical subscription tracker that receives recurring reminders.
/// Implements IReminderReceiver so it can be targeted by CreateDurableObjectReminderAsync.
///
/// Unlike a per-tick Schedule, the SAME workflow execution receives all reminder deliveries.
/// State (like reminder count) accumulates on one canonical execution across all ticks.
/// </summary>
[Workflow]
public interface ISubscriptionTracker : IReminderReceiver
{
    /// <summary>
    /// Registers an email address to receive notifications on each reminder delivery.
    /// </summary>
    [WorkflowUpdate]
    Task SubscribeAsync(string email);

    /// <summary>Returns how many reminders this tracker has received (read-only, no await).</summary>
    [WorkflowQuery]
    int GetReminderCount();
}
