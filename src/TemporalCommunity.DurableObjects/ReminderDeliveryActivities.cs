using Temporalio.Activities;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Temporal activities that deliver reminder updates to canonical DurableObjects.
/// </summary>
/// <remarks>
/// Register on a worker via
/// <see cref="DurableObjectWorkerReminderExtensions.AddDurableObjectReminderDelivery(Temporalio.Extensions.Hosting.ITemporalWorkerServiceOptionsBuilder, ReminderDeliveryActivities)"/>
/// or the DI overload. The <see cref="Temporalio.Client.ITemporalClient"/> supplied at construction
/// is the client used to issue updates — typically the same client registered in DI.
/// </remarks>
public sealed class ReminderDeliveryActivities(ITemporalClient client)
{
    /// <summary>
    /// Delivers a reminder update to the target DurableObject, using update-with-start to
    /// re-activate the object if it has deactivated.
    /// </summary>
    /// <remarks>
    /// The update ID is derived deterministically from the dispatcher workflow's ID
    /// (<see cref="ActivityExecutionContext.Current"/> → <c>Info.WorkflowId</c>), combined with
    /// the target object ID and reminder name. This ensures:
    /// <list type="bullet">
    ///   <item>Stability across activity retries — same dispatcher execution gives the same ID.</item>
    ///   <item>Uniqueness across ticks — different dispatcher executions (one per tick under
    ///   <c>ScheduleOverlapPolicy.Skip</c>) give different IDs.</item>
    /// </list>
    /// The Temporal server deduplicates on the update ID: a retry that re-issues the same ID has
    /// no additional effect. Implementations of <see cref="IReminderReceiver.OnReminderAsync"/> must
    /// still be idempotent with respect to <see cref="ReminderDeliveryContext.DeliveryId"/> because
    /// dedup is per-execution — a CAN boundary resets the server-side record.
    /// </remarks>
    /// <param name="dispatch">Payload describing the target object and reminder name.</param>
    [Activity]
    public async Task DeliverReminderAsync(ReminderDispatch dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        var deliveryId = ActivityExecutionContext.Current.Info.WorkflowId
            ?? throw new InvalidOperationException("DeliverReminderAsync must be invoked as a workflow-scheduled activity (WorkflowId was null).");
        var updateId = $"{deliveryId}:{dispatch.TargetObjectId}:{dispatch.ReminderName}";

        var startOp = WithStartWorkflowOperation.Create(
            dispatch.TargetWorkflowType,
            Array.Empty<object?>(),
            new WorkflowOptions(
                id: dispatch.TargetObjectId,
                taskQueue: dispatch.TaskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                // IdReusePolicy.AllowDuplicate is set explicitly so a previously deactivated
                // (terminated/completed) object can be re-activated by a reminder. The SDK
                // defaults to AllowDuplicate (WorkflowOptions.cs:77) but we set it explicitly
                // so the behavior is clear and does not silently break if the default changes.
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
            });

        // Wire name: "OnReminder" — SDK strips the trailing "Async" from OnReminderAsync.
        // Args: [reminderName, deliveryContext] — matches IReminderReceiver.OnReminderAsync signature.
        await client.ExecuteUpdateWithStartWorkflowAsync(
            "OnReminder",
            [dispatch.ReminderName, new ReminderDeliveryContext(deliveryId)],
            new WorkflowUpdateWithStartOptions(updateId, startOp)).ConfigureAwait(false);
    }
}
