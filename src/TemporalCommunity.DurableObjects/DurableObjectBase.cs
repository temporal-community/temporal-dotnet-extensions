using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Temporalio.Exceptions;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects.Polyfills;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Abstract base class that every DurableObject implementation must extend.
/// Owns the complete object lifecycle: activation, update processing, ContinueAsNew when history
/// grows large, and graceful deactivation with in-flight handler draining.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle admission uses process-static state keyed by workflow ID. A supported dedicated
/// Durable Object process connects to one Temporal namespace; multiple namespaces in one process
/// are unsupported.
/// </para>
/// <para>
/// Derived classes must declare the workflow run entry point, which the Temporal SDK requires
/// on the concrete type (the <c>[WorkflowRun]</c> attribute is not inheritable). The canonical
/// boilerplate is:
/// <code>
/// [WorkflowRun]
/// public Task RunAsync() => DurableObjectRunAsync();
/// </code>
/// </para>
/// <para>
/// <b>Threading model:</b> All <c>await</c> calls inside any method that executes in the
/// workflow task (called from <c>[WorkflowRun]</c>, <c>[WorkflowUpdate]</c>,
/// <c>[WorkflowQuery]</c>, or lifecycle hooks) must use <c>ConfigureAwait(true)</c>.
/// <c>ConfigureAwait(false)</c> routes continuations to the thread pool, outside the workflow's
/// <c>TaskScheduler</c>, causing <c>InvalidWorkflowSchedulerException</c> and replay divergence.
/// See the plan section "Temporal .NET workflow threading model" for a full explanation.
/// </para>
/// <para>
/// <b>Failure taxonomy (lifecycle hooks):</b> Exceptions from <c>OnActivateAsync</c> and
/// <c>OnBeforeContinueAsNewAsync</c> that are not already <c>FailureException</c> or
/// <c>OperationCanceledException</c> are wrapped and re-thrown as non-retryable
/// <c>ApplicationFailureException</c>. <c>OnTimerAsync</c> retains its narrower
/// <c>ApplicationFailureException</c> pass-through policy. <c>OnDeactivateAsync</c> exceptions
/// are swallowed and logged — deactivation must complete regardless.
/// </para>
/// <para>
/// <b>Deactivation protocol:</b> <see cref="DeactivateAsync"/> sets <c>_deactivating = true</c>
/// and returns immediately. It cannot <c>await Workflow.AllHandlersFinished</c> from inside
/// itself because it is an in-progress handler — that await would deadlock permanently.
/// Instead, <see cref="DurableObjectRunAsync"/> monitors <c>_deactivating</c> from the workflow
/// run path (which is not a handler) and executes the drain there safely.
/// </para>
/// <para>
/// <b>Drain-window gate:</b> Once <c>_deactivating</c> is <c>true</c>, any new incoming updates
/// are rejected by <c>DurableObjectWorkerInterceptor.HandleUpdateAsync</c> with a non-retryable
/// <c>ApplicationFailureException(errorType: "ObjectDeactivating")</c>. The gate is implemented
/// in the interceptor (not via SDK update validators) because there is no global validator hook.
/// </para>
/// </remarks>
public abstract class DurableObjectBase : IDurableObject
{
    // Per-workflow static registry keyed by workflowId. Allows DurableObjectWorkerInterceptor
    // to check IsDeactivating without a direct object reference. WeakReference prevents the
    // dictionary from keeping instances alive after the workflow completes.
    private static readonly ConcurrentDictionary<string, WeakReference<DurableObjectBase>> s_registry = new();

    // Set by Deactivate() helper — self-initiated completion requested from inside a handler.
    // The run loop exits after the current handler returns without draining (the handler already
    // returned, so there is nothing to drain).
    private bool _deactivated;

    // Set by DeactivateAsync() handler — external deactivation request from a caller.
    // The run loop drains all in-flight handlers before calling OnDeactivateAsync and completing.
    // Also read by DurableObjectWorkerInterceptor to reject new updates during the drain window.
    private bool _deactivating;

    // Set only after OnActivateAsync completes successfully. Updates wait for this transition;
    // queries are rejected while it is false so neither can observe partially activated state.
    private bool _activated;

    // Set before the run loop drains handlers for Continue-as-New. The interceptor rejects updates
    // that have not entered user code once this flag is visible.
    private bool _continuingAsNew;

    // Registered in-object durable timers, keyed by name. A timer keeps the object activated and
    // fires OnTimerAsync when due. Timers are durable: they survive worker crashes because
    // WaitConditionAsync replays the timeout deterministically. Re-registering an existing name
    // replaces the registration.
    private readonly Dictionary<string, TimerRegistration> _timers = new();

    // Bumped whenever a handler adds or removes a timer, so the run loop wakes and recomputes its
    // next wake deadline instead of sleeping on a now-stale one.
    private bool _timersChanged;

    // Gate for RunSerializedAsync: while one gated handler is between awaits, others wait their
    // turn. Serialization is opt-in per handler, not global (the interceptor handles global
    // serialization when serialize: true is set on DurableObjectWorkerInterceptor).
    private bool _handlerGate;

    private readonly DurableObjectOptions _options;

    /// <summary>
    /// Initializes a new instance of <see cref="DurableObjectBase"/> with default options
    /// (<see cref="DurableObjectOptions.MaxHistoryLength"/> = 10,000).
    /// </summary>
    protected DurableObjectBase() : this(new DurableObjectOptions()) { }

    /// <summary>
    /// Initializes a new instance of <see cref="DurableObjectBase"/> with the supplied options.
    /// </summary>
    /// <param name="options">
    /// Run-loop scheduling configuration. Must not be null.
    /// <see cref="DurableObjectOptions.MaxHistoryLength"/> is validated by the options record
    /// at construction time.
    /// </param>
    protected DurableObjectBase(DurableObjectOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Gets the identifier of the current Durable Object workflow execution.
    /// </summary>
    /// <remarks>
    /// Use this property instead of accessing <c>Workflow.Info.WorkflowId</c> directly from
    /// derived classes.
    /// </remarks>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This is an instance-level workflow API exposed to durable object implementations.")]
    protected string WorkflowId => Workflow.Info.WorkflowId;

    /// <summary>
    /// Whether this object has received a <see cref="DeactivateAsync"/> request and is in the
    /// drain window. Read by <c>DurableObjectWorkerInterceptor</c> to reject new updates.
    /// </summary>
    internal bool IsDeactivating => _deactivating;

    /// <summary>
    /// Whether <see cref="OnActivateAsync"/> completed successfully for this execution.
    /// </summary>
    internal bool IsActivated => _activated;

    /// <summary>
    /// Whether this execution has closed update admission and is draining for Continue-as-New.
    /// </summary>
    internal bool IsContinuingAsNew => _continuingAsNew;

    /// <summary>
    /// Attempts to get the currently running <see cref="DurableObjectBase"/> for the given
    /// workflow execution. Used by <c>DurableObjectWorkerInterceptor</c> to check
    /// <see cref="IsDeactivating"/> without holding a direct object reference.
    /// </summary>
    /// <param name="workflowId">The workflow ID of the target execution.</param>
    /// <param name="instance">
    /// The live <see cref="DurableObjectBase"/> instance, if found and still reachable.
    /// </param>
    /// <returns>
    /// <c>true</c> if the instance was found and is still alive; <c>false</c> otherwise.
    /// </returns>
    internal static bool TryGetCurrent(
        string workflowId,
        [NotNullWhen(true)] out DurableObjectBase? instance)
    {
        if (s_registry.TryGetValue(workflowId, out var weakRef) &&
            weakRef.TryGetTarget(out instance))
        {
            return true;
        }

        instance = null;
        return false;
    }

    /// <summary>
    /// Requests graceful deactivation of this object. Sets the deactivating flag and returns
    /// immediately; the run loop drains any in-flight update handlers before calling
    /// <see cref="OnDeactivateAsync"/> and completing the workflow execution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is <c>virtual</c> so subclasses may observe or augment deactivation, but
    /// prefer overriding <see cref="OnDeactivateAsync"/> for cleanup logic.
    /// </para>
    /// <para>
    /// Subclasses that override this method MUST redeclare <c>[WorkflowUpdate]</c> on the
    /// override. The Temporal SDK scans the concrete type with <c>IsDefined(attr, false)</c>
    /// and throws at registration time if a concrete override is missing the attribute.
    /// </para>
    /// <para>
    /// This method does NOT drain in-flight handlers before returning — it cannot, because it
    /// is itself an in-progress handler and <c>Workflow.AllHandlersFinished</c> would deadlock
    /// permanently from that context. The drain happens in <see cref="DurableObjectRunAsync"/>.
    /// </para>
    /// </remarks>
    [WorkflowUpdate]
    public virtual Task DeactivateAsync()
    {
        _deactivating = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Requests self-deactivation from inside the object. Sets the <c>_deactivated</c> flag so
    /// the run loop exits after the current handler returns.
    /// </summary>
    /// <remarks>
    /// Use this method in <see cref="OnActivateAsync"/> for scheduled objects (Tier 3 / explicit
    /// deactivation pattern): the object does its work on activation and then calls
    /// <see cref="Deactivate"/> to self-complete, allowing the next scheduled tick to re-activate
    /// a fresh execution. Without this, <c>ScheduleOverlapPolicy.Skip</c> suppresses subsequent
    /// ticks while the first execution remains open.
    /// </remarks>
    protected void Deactivate() => _deactivated = true;

    /// <summary>
    /// Records that handler activity has occurred. Non-virtual to preserve the invariant;
    /// override <see cref="OnActivityRecorded"/> to augment behavior (e.g. emit telemetry).
    /// </summary>
    protected void RecordActivity() => OnActivityRecorded();

    /// <summary>
    /// Called by <see cref="RecordActivity"/> when handler activity is recorded.
    /// Override to emit telemetry or perform additional bookkeeping. Must use
    /// <c>Workflow.UtcNow</c> (not <c>DateTime.UtcNow</c>) for any time reads to stay
    /// deterministic under replay.
    /// </summary>
    protected virtual void OnActivityRecorded() { }

    /// <summary>
    /// Executes an asynchronous instance activity.
    /// </summary>
    /// <typeparam name="TActivity">The registered activity implementation type.</typeparam>
    /// <param name="activityCall">An expression that invokes the activity method.</param>
    /// <param name="options">
    /// Activity execution options. Either <see cref="ActivityOptions.StartToCloseTimeout"/> or
    /// <see cref="ActivityOptions.ScheduleToCloseTimeout"/> must be set.
    /// </param>
    /// <returns>A task that completes when the activity completes.</returns>
    /// <remarks>
    /// Await the returned task directly from workflow code. A bare <c>await</c> captures the
    /// workflow scheduler, which is equivalent to <c>ConfigureAwait(true)</c>. Never append
    /// <c>ConfigureAwait(false)</c> inside workflow code.
    /// </remarks>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This is an instance-level workflow API exposed to durable object implementations.")]
    protected Task ExecuteActivityAsync<TActivity>(
        Expression<Func<TActivity, Task>> activityCall,
        ActivityOptions options) =>
        Workflow.ExecuteActivityAsync(activityCall, options);

    /// <summary>
    /// Executes an asynchronous instance activity that returns a result.
    /// </summary>
    /// <typeparam name="TActivity">The registered activity implementation type.</typeparam>
    /// <typeparam name="TResult">The activity result type.</typeparam>
    /// <param name="activityCall">An expression that invokes the activity method.</param>
    /// <param name="options">
    /// Activity execution options. Either <see cref="ActivityOptions.StartToCloseTimeout"/> or
    /// <see cref="ActivityOptions.ScheduleToCloseTimeout"/> must be set.
    /// </param>
    /// <returns>A task that completes with the activity result.</returns>
    /// <remarks>
    /// Await the returned task directly from workflow code. A bare <c>await</c> captures the
    /// workflow scheduler, which is equivalent to <c>ConfigureAwait(true)</c>. Never append
    /// <c>ConfigureAwait(false)</c> inside workflow code.
    /// </remarks>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "This is an instance-level workflow API exposed to durable object implementations.")]
    protected Task<TResult> ExecuteActivityAsync<TActivity, TResult>(
        Expression<Func<TActivity, Task<TResult>>> activityCall,
        ActivityOptions options) =>
        Workflow.ExecuteActivityAsync(activityCall, options);

    /// <summary>
    /// Registers (or re-arms) a durable timer with the given name. The timer fires
    /// <see cref="OnTimerAsync"/> when it becomes due. Timers survive worker crashes because they
    /// are driven by <c>Workflow.WaitConditionAsync</c> and replay deterministically.
    /// </summary>
    /// <param name="name">
    /// Unique name identifying this timer. Re-registering an existing name replaces the timer.
    /// </param>
    /// <param name="dueIn">
    /// Time from now until the timer fires. Must be non-negative.
    /// </param>
    /// <param name="recurring">
    /// When <c>true</c>, the timer re-arms itself with the same interval after each firing.
    /// When <c>false</c> (default), the timer fires once and is removed.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="dueIn"/> is negative.
    /// </exception>
    /// <remarks>
    /// Call from <see cref="OnActivateAsync"/> so the timer is re-established on every
    /// activation, including replay and post-ContinueAsNew runs.
    /// </remarks>
    protected void ScheduleTimer(string name, TimeSpan dueIn, bool recurring = false)
    {
        if (dueIn < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dueIn), "Timer due time cannot be negative.");
        }

        _timers[name] = new TimerRegistration(name, Workflow.UtcNow + dueIn, dueIn, recurring);
        _timersChanged = true;
    }

    /// <summary>
    /// Cancels a registered timer. No-op if the timer does not exist.
    /// </summary>
    /// <param name="name">The name of the timer to cancel.</param>
    protected void CancelTimer(string name)
    {
        if (_timers.Remove(name))
        {
            _timersChanged = true;
        }
    }

    /// <summary>
    /// Invoked when a registered timer fires. Override to react to timer events. Runs on the
    /// workflow task scheduler like any other handler, so it may await activities and child
    /// workflows but must remain deterministic.
    /// </summary>
    /// <param name="name">The name of the timer that fired, as passed to <see cref="ScheduleTimer"/>.</param>
    protected virtual Task OnTimerAsync(string name) => Task.CompletedTask;

    /// <summary>
    /// Determines whether the run loop should issue a ContinueAsNew to shed accumulated workflow
    /// history. Override to add custom triggers (for example, an update-count cadence). Must be
    /// deterministic — read only replay-safe workflow state such as
    /// <c>Workflow.ContinueAsNewSuggested</c> and <c>Workflow.Info.HistoryLength</c>.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the run loop should perform a ContinueAsNew on the next iteration.
    /// </returns>
    protected virtual bool ShouldContinueAsNew() =>
        Workflow.ContinueAsNewSuggested ||
        Workflow.CurrentHistoryLength >= _options.MaxHistoryLength;

    /// <summary>
    /// Called once when the DurableObject execution first activates (including after a
    /// ContinueAsNew). Override to initialize state, register timers via
    /// <see cref="ScheduleTimer"/>, or — for scheduled objects — call <see cref="Deactivate"/>
    /// to self-complete after doing the tick's work.
    /// </summary>
    /// <remarks>
    /// If this method throws an exception that is not already a <c>FailureException</c> or
    /// <c>OperationCanceledException</c>, the framework wraps it as a non-retryable
    /// <c>ApplicationFailureException(errorType: "ActivationFailure")</c> and terminates the
    /// workflow cleanly. Failure and cancellation exceptions retain the Temporal SDK's native
    /// terminal semantics.
    /// </remarks>
    protected virtual Task OnActivateAsync() => Task.CompletedTask;

    /// <summary>
    /// Called once when the DurableObject is deactivating — either because
    /// <see cref="DeactivateAsync"/> was requested externally or <see cref="Deactivate"/> was
    /// called internally. Override to perform cleanup (releasing leases, emitting final metrics,
    /// etc.).
    /// </summary>
    /// <remarks>
    /// Exceptions thrown from this method are swallowed and logged. Deactivation must complete
    /// regardless of cleanup failures. Do not rely on the return value of this method to confirm
    /// that cleanup succeeded.
    /// </remarks>
    protected virtual Task OnDeactivateAsync() => Task.CompletedTask;

    /// <summary>
    /// Called just before a ContinueAsNew is issued, after all in-flight handlers have been
    /// drained. Override to serialize state that must survive the history boundary. The returned
    /// collection becomes the constructor arguments of the next run. Instance fields do NOT
    /// survive ContinueAsNew; anything that must persist must be returned here and rehydrated via
    /// a <c>[WorkflowInit]</c> constructor whose parameters match the returned arguments.
    /// </summary>
    /// <returns>
    /// The constructor arguments for the new run, in the same order as the target
    /// <c>[WorkflowInit]</c> constructor. Return <see cref="Array.Empty{T}"/> (the default) if
    /// no state needs to carry forward.
    /// </returns>
    /// <remarks>
    /// If this method throws an exception that is not already a <c>FailureException</c> or
    /// <c>OperationCanceledException</c>, the framework wraps it as a non-retryable
    /// <c>ApplicationFailureException(errorType: "ContinueAsNewFailure")</c>.
    /// Failure and cancellation exceptions retain the Temporal SDK's native terminal semantics.
    /// </remarks>
    protected virtual Task<IReadOnlyCollection<object?>> OnBeforeContinueAsNewAsync() =>
        Task.FromResult<IReadOnlyCollection<object?>>(Array.Empty<object?>());

    /// <summary>
    /// The workflow run loop. Call this from the concrete <c>[WorkflowRun]</c> method:
    /// <code>
    /// [WorkflowRun]
    /// public Task RunAsync() => DurableObjectRunAsync();
    /// </code>
    /// Manages the full lifecycle: OnActivateAsync → process updates → ContinueAsNew on history
    /// threshold → drain + OnDeactivateAsync on deactivation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Tier 1 (resident) behavior:</b> Objects stay open indefinitely. When no timers are
    /// scheduled and no updates are arriving, the run loop parks at
    /// <c>Workflow.WaitConditionAsync</c> consuming no worker compute while idle. The open
    /// execution and its history remain in Temporal. Sticky-cache eviction handles idleness
    /// transparently — there is no idle-triggered CAN or passivation in v1 (Tier 2 is excluded
    /// from this release).
    /// </para>
    /// <para>
    /// <b>ContinueAsNew flow:</b> When <see cref="ShouldContinueAsNew"/> returns <c>true</c>,
    /// the loop drains in-flight handlers, calls <see cref="OnBeforeContinueAsNewAsync"/> to
    /// collect carry-forward state, then throws the CAN exception. The <c>finally</c> block
    /// removes this instance from the registry even when the CAN exception propagates through it.
    /// </para>
    /// </remarks>
    protected virtual async Task DurableObjectRunAsync()
    {
        var workflowId = Workflow.Info.WorkflowId;
        s_registry[workflowId] = new WeakReference<DurableObjectBase>(this);

        try
        {
            // Bug 3: wrap OnActivateAsync — arbitrary exceptions must terminate cleanly.
            try
            {
                await OnActivateAsync().ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not FailureException && ex is not OperationCanceledException)
            {
#pragma warning disable CA1848 // Use LoggerMessage delegates for performance
                Workflow.Logger.LogError(ex, "OnActivateAsync failed for '{WorkflowId}'", workflowId);
#pragma warning restore CA1848
                throw new ApplicationFailureException(
                    $"DurableObject activation failed: {ex.Message}",
                    ex,
                    errorType: "ActivationFailure",
                    nonRetryable: true);
            }

            _activated = true;

            while (!_deactivated && !_deactivating)
            {
                if (ShouldContinueAsNew())
                {
                    // Close admission before waiting for handlers. Updates already in user code
                    // are allowed to finish; all others are rejected by the interceptor.
                    _continuingAsNew = true;

                    // Drain handlers before CAN so no update is left dangling.
                    await Workflow.WaitConditionAsync(
                        () => Workflow.AllHandlersFinished).ConfigureAwait(true);

                    IReadOnlyCollection<object?> carryArgs;
                    try
                    {
                        carryArgs = await OnBeforeContinueAsNewAsync().ConfigureAwait(true);
                    }
                    catch (Exception ex) when (ex is not FailureException && ex is not OperationCanceledException)
                    {
#pragma warning disable CA1848 // Use LoggerMessage delegates for performance
                        Workflow.Logger.LogError(
                            ex, "OnBeforeContinueAsNewAsync failed for '{WorkflowId}'", workflowId);
#pragma warning restore CA1848
                        throw new ApplicationFailureException(
                            $"DurableObject pre-CAN hook failed: {ex.Message}",
                            ex,
                            errorType: "ContinueAsNewFailure",
                            nonRetryable: true);
                    }

                    throw Workflow.CreateContinueAsNewException(Workflow.Info.WorkflowType, carryArgs);
                }

                // Fire any timers whose due time has arrived; Bug 3 wrapping is inside.
                await FireDueTimersAsync().ConfigureAwait(true);
                if (_deactivated || _deactivating)
                {
                    break;
                }

                _timersChanged = false;
                var wakeAt = ComputeNextWakeUtc();

                if (wakeAt is null)
                {
                    await Workflow.WaitConditionAsync(
                        () => _deactivated || _deactivating || _timersChanged || ShouldContinueAsNew())
                        .ConfigureAwait(true);
                }
                else
                {
                    var remaining = wakeAt.Value - Workflow.UtcNow;
                    if (remaining < TimeSpan.Zero)
                    {
                        remaining = TimeSpan.Zero;
                    }

                    await Workflow.WaitConditionAsync(
                        () => _deactivated || _deactivating || _timersChanged || ShouldContinueAsNew(),
                        remaining).ConfigureAwait(true);
                }
            }

            // External deactivation (DeactivateAsync called): drain in-flight handlers.
            // SAFE to call from the run path — AllHandlersFinished deadlocks when awaited from
            // inside a handler (WorkflowInstance.cs:1288-1295).
            // _deactivated path (Deactivate() helper): no drain needed; handler already returned.
            if (_deactivating)
            {
                await Workflow.WaitConditionAsync(
                    () => Workflow.AllHandlersFinished).ConfigureAwait(true);
            }

            // Bug 3: swallow OnDeactivateAsync — deactivation must complete regardless.
#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                await OnDeactivateAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
#pragma warning disable CA1848 // Use LoggerMessage delegates for performance
                Workflow.Logger.LogError(ex, "OnDeactivateAsync failed for '{WorkflowId}'", workflowId);
#pragma warning restore CA1848
            }
#pragma warning restore CA1031
        }
        finally
        {
            // Always remove from registry — prevents unbounded dict growth on long-running workers.
            // Fires even when ContinueAsNewException propagates through this block.
            s_registry.TryRemove(workflowId, out _);
        }
    }

    /// <summary>
    /// Wraps a <c>Task</c>-returning handler body in an exclusive gate so that while one gated
    /// handler is between awaits, any other gated handler waits its turn. Use when you need
    /// strict turn-based (non-reentrant) execution for a specific handler.
    /// </summary>
    /// <param name="handler">The handler body to execute exclusively.</param>
    /// <remarks>
    /// <para>
    /// The workflow scheduler is single-threaded (<c>MaximumConcurrencyLevel == 1</c>) but
    /// handlers still interleave at every <c>await</c> unless something serializes them, unlike
    /// an Orleans grain. Wrap a handler body in this method to make it strictly turn-based.
    /// </para>
    /// <para>
    /// The loop-form re-check (not a single <c>if</c>) is intentional: on the legacy event-loop
    /// path, the condition pass can release all waiters at once. The loop re-checks the gate on
    /// wake; <c>MaximumConcurrencyLevel == 1</c> makes check-then-set atomic.
    /// </para>
    /// <para>
    /// When <c>DurableObjectWorkerInterceptor</c> is configured with <c>serialize: true</c>,
    /// updates are serialized against each other, but <see cref="OnTimerAsync"/> callbacks
    /// still run while an update is suspended at an <c>await</c>. The interceptor gate and this
    /// method's gate are separate: to make a timer callback take turns with an update, wrap
    /// both the update body and the <see cref="OnTimerAsync"/> body in this method.
    /// </para>
    /// </remarks>
    protected async Task RunSerializedAsync(Func<Task> handler)
    {
        Throw.IfNull(handler, nameof(handler));

        while (_handlerGate)
        {
            await Workflow.WaitConditionAsync(() => !_handlerGate).ConfigureAwait(true);
        }

        _handlerGate = true;
        try
        {
            await handler().ConfigureAwait(true);
        }
        finally
        {
            _handlerGate = false;
        }
    }

    /// <summary>
    /// Wraps a <c>Task&lt;T&gt;</c>-returning handler body in an exclusive gate so that while
    /// one gated handler is between awaits, any other gated handler waits its turn. Use when you
    /// need strict turn-based (non-reentrant) execution for a specific handler that returns a value.
    /// </summary>
    /// <typeparam name="T">The return type of the handler.</typeparam>
    /// <param name="handler">The handler body to execute exclusively.</param>
    /// <returns>The value returned by <paramref name="handler"/>.</returns>
    /// <remarks>See <see cref="RunSerializedAsync(Func{Task})"/> for full documentation.</remarks>
    protected async Task<T> RunSerializedAsync<T>(Func<Task<T>> handler)
    {
        Throw.IfNull(handler, nameof(handler));

        while (_handlerGate)
        {
            await Workflow.WaitConditionAsync(() => !_handlerGate).ConfigureAwait(true);
        }

        _handlerGate = true;
        try
        {
            return await handler().ConfigureAwait(true);
        }
        finally
        {
            _handlerGate = false;
        }
    }

    /// <summary>
    /// Fires all registered timers whose due time has passed. Recurring timers re-arm; one-shot
    /// timers are removed. Fires in deterministic name order so replay is consistent.
    /// Each timer invocation is wrapped per Bug 3: arbitrary exceptions become non-retryable
    /// <c>ApplicationFailureException</c>.
    /// </summary>
    private async Task FireDueTimersAsync()
    {
        var now = Workflow.UtcNow;

        // Order by name for deterministic fire order under replay — Dictionary enumeration order
        // is not guaranteed stable.
        var due = _timers.Values
            .Where(t => t.NextDueUtc <= now)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var timer in due)
        {
            if (timer.Recurring)
            {
                timer.NextDueUtc = now + timer.Interval;
            }
            else
            {
                _timers.Remove(timer.Name);
            }

            try
            {
                await OnTimerAsync(timer.Name).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not ApplicationFailureException)
            {
#pragma warning disable CA1848 // Use LoggerMessage delegates for performance
                Workflow.Logger.LogError(
                    ex,
                    "OnTimerAsync('{TimerName}') failed for '{WorkflowId}'",
                    timer.Name,
                    Workflow.Info.WorkflowId);
#pragma warning restore CA1848
                throw new ApplicationFailureException(
                    $"DurableObject timer '{timer.Name}' failed: {ex.Message}",
                    ex,
                    errorType: "TimerFailure",
                    nonRetryable: true);
            }

            if (_deactivated || _deactivating)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Computes the next UTC time at which the run loop must wake to fire a due timer.
    /// Returns <c>null</c> when no timers are scheduled, meaning the loop may wait indefinitely.
    /// </summary>
    private DateTime? ComputeNextWakeUtc()
    {
        if (_timers.Count == 0)
        {
            return null;
        }

        return _timers.Values.Min(t => t.NextDueUtc);
    }

    /// <summary>Tracks the state of a single registered durable timer.</summary>
    private sealed class TimerRegistration
    {
        /// <summary>Initializes a new timer registration.</summary>
        /// <param name="name">The timer's unique name.</param>
        /// <param name="nextDueUtc">The UTC time at which this timer next fires.</param>
        /// <param name="interval">The interval for recurring timers; ignored for one-shot timers.</param>
        /// <param name="recurring">Whether the timer re-arms itself after each firing.</param>
        public TimerRegistration(string name, DateTime nextDueUtc, TimeSpan interval, bool recurring)
        {
            Name = name;
            NextDueUtc = nextDueUtc;
            Interval = interval;
            Recurring = recurring;
        }

        /// <summary>Gets the timer's unique name.</summary>
        public string Name { get; }

        /// <summary>Gets or sets the UTC time at which this timer next fires.</summary>
        public DateTime NextDueUtc { get; set; }

        /// <summary>Gets the configured interval (for recurring timers).</summary>
        public TimeSpan Interval { get; }

        /// <summary>Gets whether the timer re-arms after each firing.</summary>
        public bool Recurring { get; }
    }
}
