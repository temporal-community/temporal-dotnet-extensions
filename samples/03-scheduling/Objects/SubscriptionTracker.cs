#pragma warning disable CA1822 // Workflow methods must be instance methods
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.Scheduling.Activities;

namespace TemporalCommunity.DurableObjects.Scheduling.Objects;

public sealed record SubscriptionState(string Email, int ReminderCount, HashSet<string> CompletedDeliveries);

/// <summary>A canonical object whose subscription and reminder receipts survive Continue-as-New.</summary>
[Workflow]
public sealed class SubscriptionTracker : DurableObjectBase<SubscriptionState>, ISubscriptionTracker
{
    [WorkflowInit]
    public SubscriptionTracker(DurableObjectSnapshot<SubscriptionState>? snapshot = null)
        : base(snapshot, new SubscriptionState(string.Empty, 0, [])) { }

    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<SubscriptionState>? snapshot = null) => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task SubscribeAsync(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        State = State with { Email = email };
        Workflow.Logger.LogInformation("SubscriptionTracker: subscribed {Email}", email);
        return Task.CompletedTask;
    }

    [WorkflowUpdate]
    public async Task OnReminderAsync(string reminderName, ReminderDeliveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var receipt = $"{reminderName}/{context.DeliveryId}";
        if (State.CompletedDeliveries.Contains(receipt)) return;

        if (!string.IsNullOrEmpty(State.Email))
        {
            // The downstream service must honor this key too: an activity can retry after
            // its effect succeeded but before Temporal recorded its result.
            await ExecuteActivityAsync(
                (SchedulingActivities act) => act.NotifySubscriberAsync(State.Email, context.DeliveryId),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
        }

        // A failed notification must not mark a delivery complete. Retain successful keys
        // in the snapshot so delayed duplicates are rejected even after newer deliveries.
        State.CompletedDeliveries.Add(receipt);
        State = State with { ReminderCount = State.ReminderCount + 1 };
        Workflow.Logger.LogInformation(
            "SubscriptionTracker: received reminder '{Name}' (deliveryId={DeliveryId}, count={Count})",
            reminderName, context.DeliveryId, State.ReminderCount);
    }

    [WorkflowQuery]
    public int GetReminderCount() => State.ReminderCount;
}
