using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Client.Schedules;
using TemporalCommunity.DurableObjects.Polyfills;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Client-side helpers for creating Temporal Schedules that activate DurableObjects on a cadence.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Schedule-based activation (<see cref="CreateDurableObjectScheduleAsync{T}"/>):</strong>
/// Each tick spawns a fresh, time-suffixed workflow execution — useful for periodic background work
/// that does not need to accumulate state on one canonical object (e.g., "run report every hour").
/// Scheduled object implementations must call <c>Deactivate()</c> at the end of
/// <c>OnActivateAsync</c> so the execution completes after each tick; without this,
/// <see cref="ScheduleOverlapPolicy.Skip"/> will suppress subsequent ticks while the first
/// execution is still open.
/// </para>
/// <para>
/// <strong>Canonical reminder delivery (<see cref="CreateDurableObjectReminderAsync{T}"/>):</strong>
/// Each tick spawns a short-lived <c>ReminderDispatcher</c> workflow that delivers an
/// <c>OnReminder</c> update-with-start to the named canonical object, re-activating it if it has
/// deactivated. State accumulates on a single execution — unlike schedule-based activation.
/// Requires <c>ReminderDispatcher</c> and <see cref="ReminderDeliveryActivities"/> to be registered
/// on the worker.
/// </para>
/// <para>
/// Both methods are also available on <see cref="IDurableObjectFactory"/> when injected via DI.
/// </para>
/// </remarks>
public static class DurableObjectSchedule
{
    /// <summary>
    /// Creates a Temporal Schedule that activates the DurableObject type of
    /// <typeparamref name="T"/> on the given <paramref name="spec"/> cadence.
    /// </summary>
    /// <remarks>
    /// Each tick spawns a fresh, time-suffixed workflow execution (Temporal always appends the
    /// scheduled time to the workflow ID for uniqueness — a fixed ID is not possible for schedule
    /// actions). <typeparamref name="T"/> must be the DurableObject contract interface; the
    /// concrete workflow type is resolved via <see cref="DurableObjectNaming.ResolveWorkflowType"/>.
    /// <para>
    /// <strong>Self-completion required:</strong> Implementations must call <c>Deactivate()</c> at
    /// the end of <c>OnActivateAsync</c>. Without self-completion, <see cref="ScheduleOverlapPolicy.Skip"/>
    /// (the default) will suppress subsequent ticks once the first execution remains open.
    /// See <c>docs/BOILERPLATE.md</c> under "Scheduled Objects."
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The DurableObject contract interface type.</typeparam>
    /// <param name="client">The Temporal client to use for schedule creation.</param>
    /// <param name="scheduleId">The unique ID for this Temporal Schedule.</param>
    /// <param name="objectId">
    /// The base workflow ID; Temporal appends the scheduled time for each tick's execution.
    /// </param>
    /// <param name="spec">The cadence specification (intervals, cron expressions, etc.).</param>
    /// <param name="taskQueue">The task queue on which the DurableObject worker runs.</param>
    /// <param name="overlap">
    /// Policy governing overlapping ticks. Defaults to <see cref="ScheduleOverlapPolicy.Skip"/>.
    /// </param>
    /// <param name="scheduleOptions">Additional schedule creation options, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to cancel the schedule creation RPC.</param>
    /// <returns>A handle to the created schedule.</returns>
    public static Task<ScheduleHandle> CreateDurableObjectScheduleAsync<T>(
        this ITemporalClient client,
        string scheduleId,
        string objectId,
        ScheduleSpec spec,
        string taskQueue,
        ScheduleOverlapPolicy overlap = ScheduleOverlapPolicy.Skip,
        ScheduleOptions? scheduleOptions = null,
        CancellationToken cancellationToken = default)
        where T : IDurableObject
    {
        Throw.IfNull(client, nameof(client));
        // Bug 1 fix: use DurableObjectNaming.ResolveWorkflowType instead of inline I-strip.
        var workflowType = DurableObjectNaming.ResolveWorkflowType(typeof(T));

        var action = ScheduleActionStartWorkflow.Create(
            workflowType,
            Array.Empty<object?>(),
            new WorkflowOptions(id: objectId, taskQueue: taskQueue));

        var schedule = new Schedule(action, spec)
        {
            Policy = new SchedulePolicy { Overlap = overlap },
        };

        // Thread the CancellationToken via ScheduleOptions.Rpc — CreateScheduleAsync does not
        // accept a CancellationToken directly; callers supply it through RpcOptions.
        var effectiveOptions = WithCancellation(scheduleOptions, cancellationToken);
        return client.CreateScheduleAsync(scheduleId, schedule, effectiveOptions);
    }

    /// <summary>
    /// Creates a Temporal Schedule that delivers a recurring reminder to the canonical
    /// DurableObject identified by <paramref name="targetObjectId"/>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="CreateDurableObjectScheduleAsync{T}"/>, which spawns a fresh execution per
    /// tick, this method delivers the reminder as an update-with-start to a single canonical object.
    /// State accumulates on one workflow execution — the object is re-activated if it has
    /// deactivated. Each tick spawns a short-lived <c>ReminderDispatcher</c> workflow whose activity
    /// issues the <c>OnReminder</c> update.
    /// <para>
    /// <strong>Worker prerequisites:</strong> The worker must have <c>ReminderDispatcher</c>
    /// registered as a workflow (via <c>AddDurableObjectWorkflows</c>) and
    /// <see cref="ReminderDeliveryActivities"/> registered as activities (via
    /// <see cref="DurableObjectWorkerReminderExtensions.AddDurableObjectReminderDelivery(Temporalio.Extensions.Hosting.ITemporalWorkerServiceOptionsBuilder, ReminderDeliveryActivities)"/>).
    /// </para>
    /// </remarks>
    /// <typeparam name="T">
    /// The target DurableObject contract interface type. Must implement <see cref="IReminderReceiver"/>.
    /// </typeparam>
    /// <param name="client">The Temporal client to use for schedule creation.</param>
    /// <param name="scheduleId">The unique ID for this Temporal Schedule.</param>
    /// <param name="targetObjectId">
    /// The canonical object's workflow ID. The reminder will always be delivered to this specific
    /// execution (re-activating it via update-with-start if it has deactivated).
    /// </param>
    /// <param name="reminderName">
    /// The reminder name passed to <see cref="IReminderReceiver.OnReminderAsync"/>.
    /// </param>
    /// <param name="spec">The cadence specification.</param>
    /// <param name="taskQueue">The task queue on which the DurableObject worker runs.</param>
    /// <param name="overlap">
    /// Policy governing overlapping ticks. Defaults to <see cref="ScheduleOverlapPolicy.Skip"/>.
    /// </param>
    /// <param name="scheduleOptions">Additional schedule creation options, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to cancel the schedule creation RPC.</param>
    /// <returns>A handle to the created schedule.</returns>
    public static Task<ScheduleHandle> CreateDurableObjectReminderAsync<T>(
        this ITemporalClient client,
        string scheduleId,
        string targetObjectId,
        string reminderName,
        ScheduleSpec spec,
        string taskQueue,
        ScheduleOverlapPolicy overlap = ScheduleOverlapPolicy.Skip,
        ScheduleOptions? scheduleOptions = null,
        CancellationToken cancellationToken = default)
        where T : IReminderReceiver
    {
        Throw.IfNull(client, nameof(client));
        // Bug 1 fix: use DurableObjectNaming.ResolveWorkflowType instead of inline I-strip.
        var workflowType = DurableObjectNaming.ResolveWorkflowType(typeof(T));

        var dispatch = new ReminderDispatch(targetObjectId, workflowType, taskQueue, reminderName);

        // ReminderDispatcher is an internal Phase 3 type. Reference it by its string workflow type
        // name to avoid a compile-time dependency on an internal type from Phase 4 code.
        var action = ScheduleActionStartWorkflow.Create(
            "ReminderDispatcher",
            [dispatch],
            new WorkflowOptions(id: $"reminder-{scheduleId}", taskQueue: taskQueue));

        var schedule = new Schedule(action, spec)
        {
            Policy = new SchedulePolicy { Overlap = overlap },
        };

        var effectiveOptions = WithCancellation(scheduleOptions, cancellationToken);
        return client.CreateScheduleAsync(scheduleId, schedule, effectiveOptions);
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns a <see cref="ScheduleOptions"/> with the <paramref name="cancellationToken"/>
    /// injected via <see cref="RpcOptions"/>. Returns <paramref name="options"/> unchanged when
    /// the token is <see cref="CancellationToken.None"/>.
    /// </summary>
    private static ScheduleOptions? WithCancellation(
        ScheduleOptions? options,
        CancellationToken cancellationToken)
    {
        if (cancellationToken == CancellationToken.None)
        {
            return options;
        }

        var result = options is null ? new ScheduleOptions() : (ScheduleOptions)options.Clone();
        result.Rpc ??= new RpcOptions();
        result.Rpc.CancellationToken = cancellationToken;
        return result;
    }
}
