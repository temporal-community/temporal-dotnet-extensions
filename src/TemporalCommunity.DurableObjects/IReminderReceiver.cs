using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Optional interface for DurableObjects that want to receive scheduled reminders delivered to
/// their canonical identity. Objects that implement this can be targeted by
/// <c>CreateDurableObjectReminderAsync&lt;T&gt;()</c>.
/// </summary>
/// <remarks>
/// Unlike a plain Temporal Schedule (which spawns a fresh time-suffixed execution per tick), a
/// reminder is delivered as an update-with-start: it re-activates the object if it has deactivated
/// and otherwise applies to the live instance, accumulating state on one canonical execution.
/// Implementations should be idempotent with respect to <see cref="ReminderDeliveryContext.DeliveryId"/>
/// because a reminder may be re-delivered across ContinueAsNew boundaries.
/// </remarks>
public interface IReminderReceiver : IDurableObject
{
    /// <summary>
    /// Called when a scheduled reminder is delivered to this object.
    /// </summary>
    /// <param name="reminderName">
    /// The name of the reminder as registered via <c>CreateDurableObjectReminderAsync&lt;T&gt;()</c>.
    /// </param>
    /// <param name="context">
    /// Delivery context carrying the <see cref="ReminderDeliveryContext.DeliveryId"/> — a value
    /// stable across retries of the same tick and unique across scheduled ticks. Use it to detect
    /// and skip duplicate deliveries across ContinueAsNew boundaries.
    /// </param>
    [WorkflowUpdate]
    Task OnReminderAsync(string reminderName, ReminderDeliveryContext context);
}
