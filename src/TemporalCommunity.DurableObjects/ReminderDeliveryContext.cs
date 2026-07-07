namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Delivery context passed to <see cref="IReminderReceiver.OnReminderAsync"/> when a scheduled
/// reminder is delivered to a DurableObject.
/// </summary>
/// <param name="DeliveryId">
/// The dispatcher workflow's ID. Stable across activity retries for the same tick (same workflow
/// execution), and unique across ticks under <c>ScheduleOverlapPolicy.Skip</c> (different
/// dispatcher executions). Use this value to detect and skip duplicate deliveries that may occur
/// if the target object performs a ContinueAsNew between a failed delivery activity and its retry:
/// the new execution has no record of the prior <c>UpdateId</c>, so the delivery re-runs, but
/// the <see cref="DeliveryId"/> will be the same. Handlers should store the last-seen
/// <see cref="DeliveryId"/> per reminder name and skip processing when it repeats.
/// </param>
public sealed record ReminderDeliveryContext(string DeliveryId);
