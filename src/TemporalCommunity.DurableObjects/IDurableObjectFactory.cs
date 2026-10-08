using System.Diagnostics.CodeAnalysis;
using Temporalio.Api.Enums.V1;
using Temporalio.Client.Schedules;

namespace TemporalCommunity.DurableObjects;

#pragma warning disable CA1716 // 'Get' conflicts with reserved keyword in other languages — intentional API design decision; IDurableObjectFactory is C#-first

/// <summary>
/// Primary entry point for callers interacting with DurableObjects from client code.
/// Register via <see cref="DurableObjectServiceCollectionExtensions.AddDurableObjects"/> and
/// inject into application services.
/// </summary>
/// <remarks>
/// A <c>DefaultTaskQueue</c> is set at registration time (via
/// <see cref="DurableObjectServiceCollectionExtensions.AddDurableObjects"/>). Single-argument
/// overloads of <see cref="Get{T}(string)"/> and <see cref="GetOrCreateAsync{T}(string, CancellationToken)"/>
/// use that default; two-argument overloads allow per-call task-queue override for multi-queue
/// scenarios.
/// </remarks>
public interface IDurableObjectFactory
{
    /// <summary>
    /// Returns a typed proxy for the DurableObject with the given ID using the default task queue.
    /// No RPC is issued at proxy creation time; the proxy is purely a local dispatch facade.
    /// </summary>
    /// <typeparam name="T">The DurableObject interface type.</typeparam>
    /// <param name="objectId">The workflow ID that identifies the DurableObject instance.</param>
    /// <returns>A typed proxy implementing <typeparamref name="T"/>.</returns>
    T Get<T>(string objectId) where T : IDurableObject;

    /// <summary>
    /// Returns a typed proxy for the DurableObject with the given ID on the specified task queue.
    /// No RPC is issued at proxy creation time.
    /// </summary>
    /// <typeparam name="T">The DurableObject interface type.</typeparam>
    /// <param name="objectId">The workflow ID that identifies the DurableObject instance.</param>
    /// <param name="taskQueue">
    /// The task queue the target worker polls. Overrides the factory's default task queue for
    /// this call.
    /// </param>
    /// <returns>A typed proxy implementing <typeparamref name="T"/>.</returns>
    T Get<T>(string objectId, string taskQueue) where T : IDurableObject;

    /// <summary>Returns a typed proxy whose queries and updates use the supplied call options.</summary>
    T Get<T>(string objectId, DurableObjectCallOptions callOptions) where T : IDurableObject;

    /// <summary>Returns a typed proxy for a task queue using the supplied call options.</summary>
    T Get<T>(string objectId, string taskQueue, DurableObjectCallOptions callOptions)
        where T : IDurableObject;

    /// <summary>
    /// Ensures a DurableObject execution exists for the given ID (starting one if needed),
    /// then returns a typed proxy. Uses the default task queue.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Get{T}(string)"/>, this method issues a Temporal RPC to guarantee the
    /// workflow execution exists before returning. The start is race-free: concurrent callers with
    /// the same <paramref name="objectId"/> result in exactly one execution.
    /// </remarks>
    /// <typeparam name="T">The DurableObject interface type.</typeparam>
    /// <param name="objectId">The workflow ID that identifies the DurableObject instance.</param>
    /// <param name="cancellationToken">Token to cancel the start RPC.</param>
    /// <returns>A typed proxy for the (now-guaranteed-running) DurableObject.</returns>
    Task<T> GetOrCreateAsync<T>(string objectId, CancellationToken cancellationToken = default)
        where T : IDurableObject;

    /// <summary>
    /// Ensures a DurableObject execution exists for the given ID on the specified task queue,
    /// then returns a typed proxy.
    /// </summary>
    /// <typeparam name="T">The DurableObject interface type.</typeparam>
    /// <param name="objectId">The workflow ID that identifies the DurableObject instance.</param>
    /// <param name="taskQueue">
    /// The task queue the target worker polls. Overrides the factory's default task queue.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the start RPC.</param>
    /// <returns>A typed proxy for the (now-guaranteed-running) DurableObject.</returns>
    Task<T> GetOrCreateAsync<T>(string objectId, string taskQueue, CancellationToken cancellationToken = default)
        where T : IDurableObject;

    /// <summary>Ensures an object exists and returns a proxy using the supplied call options.</summary>
    Task<T> GetOrCreateAsync<T>(string objectId, DurableObjectCallOptions callOptions)
        where T : IDurableObject;

    /// <summary>Ensures an object exists on a task queue and returns a proxy using the supplied call options.</summary>
    Task<T> GetOrCreateAsync<T>(
        string objectId,
        string taskQueue,
        DurableObjectCallOptions callOptions)
        where T : IDurableObject;

    /// <summary>
    /// Executes a named query against a running DurableObject execution and returns the result.
    /// </summary>
    /// <remarks>
    /// This is the non-blocking async escape hatch for queries. The typed proxy exposes queries as
    /// synchronous methods (e.g., <c>int GetCount()</c>) which must block a thread internally;
    /// use this method on hot paths or wherever thread-parking is unacceptable.
    /// Throws <see cref="DurableObjectNotFoundException"/> if the object does not exist, or
    /// <see cref="DurableObjectNotActiveException"/> if it has been deactivated.
    /// </remarks>
    /// <typeparam name="TResult">The expected query result type.</typeparam>
    /// <param name="objectId">The workflow ID of the target DurableObject.</param>
    /// <param name="queryName">The registered query wire name (e.g., <c>"GetCount"</c>).</param>
    /// <param name="args">Query arguments, if any. Pass <see langword="null"/> for zero-argument queries.</param>
    /// <param name="cancellationToken">Token to cancel the query RPC.</param>
    /// <returns>The deserialized query result.</returns>
    Task<TResult> QueryDurableObjectAsync<TResult>(
        string objectId,
        string queryName,
        object?[]? args = null,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a named query using immutable per-call transport options.</summary>
    Task<TResult> QueryDurableObjectAsync<TResult>(
        string objectId,
        string queryName,
        object?[]? args,
        DurableObjectCallOptions callOptions);

    /// <summary>
    /// Executes a named query, returning <see langword="default"/> if the object does not exist
    /// or is not active, rather than throwing.
    /// </summary>
    /// <remarks>
    /// This is the try-get variant of the named async query API. Still
    /// side-effect-free — never starts a workflow. Use when "no such object" is an expected,
    /// normal outcome rather than an error condition.
    /// </remarks>
    /// <typeparam name="TResult">The expected query result type.</typeparam>
    /// <param name="objectId">The workflow ID of the target DurableObject.</param>
    /// <param name="queryName">The registered query wire name.</param>
    /// <param name="args">Query arguments, if any.</param>
    /// <param name="cancellationToken">Token to cancel the query RPC.</param>
    /// <returns>
    /// The deserialized query result, or <see langword="default"/> if the object does not exist
    /// or is not active.
    /// </returns>
    [return: MaybeNull]
    Task<TResult> QueryOrDefaultAsync<TResult>(
        string objectId,
        string queryName,
        object?[]? args = null,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a named query with call options, returning default when inactive or absent.</summary>
    [return: MaybeNull]
    Task<TResult> QueryOrDefaultAsync<TResult>(
        string objectId,
        string queryName,
        object?[]? args,
        DurableObjectCallOptions callOptions);

    /// <summary>
    /// Streams the workflow IDs of all DurableObjects of type <typeparamref name="T"/> by
    /// querying Temporal visibility. Results are yielded as they arrive — no buffering.
    /// </summary>
    /// <remarks>
    /// This method is available on the <c>net8.0</c> and <c>net10.0</c> assets. The
    /// <c>netstandard2.1</c> asset throws <see cref="PlatformNotSupportedException"/> because
    /// the Temporal SDK visibility API is not exposed on that target.
    ///
    /// Visibility is eventually consistent: a just-created object may take a moment to appear.
    /// Results may include time-suffixed executions created by schedule-based activation;
    /// callers who need to distinguish canonical objects from scheduled one-shots should use
    /// a custom Search Attribute. See <c>docs/tier-model.md</c> for details.
    /// </remarks>
    /// <typeparam name="T">The DurableObject interface type to enumerate.</typeparam>
    /// <param name="runningOnly">
    /// When <see langword="true"/> (the default), only currently-running executions are included.
    /// Pass <see langword="false"/> to include completed, terminated, and timed-out executions.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the visibility enumeration.</param>
    /// <returns>An async stream of workflow IDs.</returns>
    IAsyncEnumerable<string> ListDurableObjectsAsync<T>(
        bool runningOnly = true,
        CancellationToken cancellationToken = default)
        where T : IDurableObject;

    /// <summary>
    /// Streams rich visibility metadata for DurableObject executions of type <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// By default only running canonical objects are returned. Set
    /// <see cref="DurableObjectListOptions.IncludeScheduled"/> to include executions created by
    /// Temporal Schedules. Visibility is eventually consistent. This API is unavailable in the
    /// <c>netstandard2.1</c> asset.
    /// </remarks>
    /// <typeparam name="T">The DurableObject contract type.</typeparam>
    /// <param name="options">Listing and classification options, or null for defaults.</param>
    /// <param name="cancellationToken">Token to cancel visibility enumeration.</param>
    /// <returns>An async stream of execution descriptors.</returns>
    IAsyncEnumerable<DurableObjectExecutionInfo> ListDurableObjectExecutionsAsync<T>(
        DurableObjectListOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : IDurableObject;

    /// <summary>
    /// Creates a Temporal Schedule that activates a fresh DurableObject execution per tick.
    /// </summary>
    /// <remarks>
    /// Each tick spawns a fresh, time-suffixed workflow execution. Use for periodic background
    /// work that does not accumulate state on one canonical object. Implementations must call
    /// <c>Deactivate()</c> at the end of <c>OnActivateAsync</c> so the execution self-completes
    /// after each tick — without this, <see cref="ScheduleOverlapPolicy.Skip"/> will suppress
    /// subsequent ticks while the first execution remains open.
    /// </remarks>
    /// <typeparam name="T">The DurableObject contract interface type.</typeparam>
    /// <param name="scheduleId">The unique ID for this Temporal Schedule.</param>
    /// <param name="objectId">
    /// The base workflow ID; Temporal appends the scheduled time for each tick's execution.
    /// </param>
    /// <param name="spec">The cadence specification.</param>
    /// <param name="taskQueue">The task queue on which the DurableObject worker runs.</param>
    /// <param name="overlap">
    /// Policy governing overlapping ticks. Defaults to <see cref="ScheduleOverlapPolicy.Skip"/>.
    /// </param>
    /// <param name="scheduleOptions">Additional schedule creation options, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to cancel the schedule creation RPC.</param>
    /// <returns>A handle to the created schedule.</returns>
    Task<ScheduleHandle> CreateDurableObjectScheduleAsync<T>(
        string scheduleId,
        string objectId,
        ScheduleSpec spec,
        string taskQueue,
        ScheduleOverlapPolicy overlap = ScheduleOverlapPolicy.Skip,
        ScheduleOptions? scheduleOptions = null,
        CancellationToken cancellationToken = default)
        where T : IDurableObject;

    /// <summary>
    /// Creates a Temporal Schedule that delivers recurring reminders to a canonical DurableObject.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="CreateDurableObjectScheduleAsync{T}"/>, which spawns fresh executions,
    /// this delivers each reminder as an update-with-start to the canonical object identified by
    /// <paramref name="targetObjectId"/>. State accumulates on one workflow execution; the object
    /// is re-activated if it has deactivated. Requires <c>ReminderDispatcher</c> and
    /// <see cref="ReminderDeliveryActivities"/> to be registered on the worker.
    /// </remarks>
    /// <typeparam name="T">
    /// The target DurableObject contract interface type. Must implement <see cref="IReminderReceiver"/>.
    /// </typeparam>
    /// <param name="scheduleId">The unique ID for this Temporal Schedule.</param>
    /// <param name="targetObjectId">The canonical object's workflow ID.</param>
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
    Task<ScheduleHandle> CreateDurableObjectReminderAsync<T>(
        string scheduleId,
        string targetObjectId,
        string reminderName,
        ScheduleSpec spec,
        string taskQueue,
        ScheduleOverlapPolicy overlap = ScheduleOverlapPolicy.Skip,
        ScheduleOptions? scheduleOptions = null,
        CancellationToken cancellationToken = default)
        where T : IReminderReceiver;
}

#pragma warning restore CA1716
