namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Payload passed from a Temporal Schedule to <see cref="ReminderDispatcher"/> on each tick.
/// The dispatcher workflow executes a <c>ReminderDeliveryActivities.DeliverReminderAsync</c>
/// activity with this payload, which then issues the <c>OnReminder</c> update to the target
/// canonical object.
/// </summary>
/// <param name="TargetObjectId">The canonical object's workflow ID.</param>
/// <param name="TargetWorkflowType">
/// The workflow type of the target DurableObject (used to start-if-not-running via
/// <c>update-with-start</c>).
/// </param>
/// <param name="TaskQueue">The task queue on which the target DurableObject runs.</param>
/// <param name="ReminderName">
/// The reminder name to deliver to <c>IReminderReceiver.OnReminderAsync</c>.
/// </param>
/// <remarks>
/// Positional constructor order must not change without updating all call sites: the Temporal SDK
/// serializes this record positionally when it is stored as a Schedule action argument.
/// </remarks>
public sealed record ReminderDispatch(
    string TargetObjectId,
    string TargetWorkflowType,
    string TaskQueue,
    string ReminderName);
