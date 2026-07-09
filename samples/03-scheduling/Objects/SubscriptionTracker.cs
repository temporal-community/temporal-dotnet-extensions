#pragma warning disable CA1822 // Workflow methods must be instance methods
#pragma warning disable CA2007 // ConfigureAwait — workflow code must use ConfigureAwait(true), never false
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.Scheduling.Activities;

namespace TemporalCommunity.DurableObjects.Scheduling.Objects;

/// <summary>
/// Canonical subscription tracker that receives recurring reminder deliveries.
///
/// Pattern: Canonical Reminder (one persistent execution, state accumulates)
/// - The SAME workflow execution receives every reminder tick (unlike per-tick Schedule)
/// - State (reminder count, email) persists across all reminder deliveries
/// - The object is re-activated via update-with-start if it has previously deactivated
/// - context.DeliveryId is stable across activity retries for the same tick, and unique across ticks:
///   use it to detect and skip duplicate deliveries that can occur across ContinueAsNew boundaries
/// </summary>
[Workflow]
public sealed class SubscriptionTracker : DurableObjectBase, ISubscriptionTracker
{
    private string _email = string.Empty;
    private int _reminderCount;

    // Track the last-seen DeliveryId per reminder name to guard against duplicates
    // that can occur across ContinueAsNew boundaries.
    private readonly Dictionary<string, string> _lastDeliveryIds = new();

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    /// <inheritdoc/>
    [WorkflowUpdate]
    public Task SubscribeAsync(string email)
    {
        _email = email;
        Workflow.Logger.LogInformation("SubscriptionTracker: subscribed {Email}", email);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the framework when a scheduled reminder is delivered.
    /// Uses context.DeliveryId for idempotency: the same DeliveryId arriving twice (e.g., after
    /// a ContinueAsNew) is skipped so the notification is sent exactly once per tick.
    /// </summary>
    [WorkflowUpdate]
    public async Task OnReminderAsync(string reminderName, ReminderDeliveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Idempotency guard: skip if we already processed this delivery in a prior run.
        // The DeliveryId is stable across retries but unique per tick, so this detects
        // duplicate deliveries that can occur after ContinueAsNew boundaries.
        if (_lastDeliveryIds.TryGetValue(reminderName, out var lastId) && lastId == context.DeliveryId)
        {
            Workflow.Logger.LogInformation(
                "SubscriptionTracker: skipping duplicate reminder '{Name}' (deliveryId={DeliveryId})",
                reminderName, context.DeliveryId);
            return;
        }

        _lastDeliveryIds[reminderName] = context.DeliveryId;
        _reminderCount++;

        Workflow.Logger.LogInformation(
            "SubscriptionTracker: received reminder '{Name}' (deliveryId={DeliveryId}, count={Count})",
            reminderName, context.DeliveryId, _reminderCount);

        if (!string.IsNullOrEmpty(_email))
        {
            await Workflow.ExecuteActivityAsync(
                (SchedulingActivities act) => act.NotifySubscriberAsync(_email, context.DeliveryId),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) })
                .ConfigureAwait(true);
        }
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public int GetReminderCount() => _reminderCount;
}
