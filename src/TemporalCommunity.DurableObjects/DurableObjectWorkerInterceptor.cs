using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Temporalio.Exceptions;
using Temporalio.Worker.Interceptors;
using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Worker interceptor that installs activation admission, authorization, serialization
/// (non-reentrant by default), lifecycle admission, and an exception safety net. Installed
/// automatically by
/// <see cref="DurableObjectWorkerExtensions.AddDurableObjectWorkflows(Temporalio.Extensions.Hosting.ITemporalWorkerServiceOptionsBuilder, System.Reflection.Assembly, DurableObjectWorkerOptions?)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle admission uses the SDK's execution-local instance, including before the run method
/// starts. The supported dedicated Durable Object topology remains one Temporal namespace per
/// process; multiple namespaces in one process are unsupported.
/// </para>
/// <para>
/// <b>Responsibilities (order matters inside <c>HandleUpdateAsync</c>):</b>
/// <list type="number">
///   <item><description>
///     <b>Activation admission</b> — updates wait until activation completes.
///   </description></item>
///   <item><description>
///     <b>Continue-as-New admission</b> — updates arriving after rollover starts fail before
///     authorization or user code.
///   </description></item>
///   <item><description>
///     <b>Authorization</b> — runs before serialization so rejected updates do not consume the gate.
///   </description></item>
///   <item><description>
///     <b>Serialization</b> — while one handler is between awaits, subsequent updates wait.
///     Uses a <c>while</c> loop (not <c>if</c>) for correct behavior when multiple waiters are
///     released simultaneously (see SDK <c>WorkflowInstance.cs:897-908</c>).
///   </description></item>
///   <item><description>
///     <b>Drain-window gate</b> — runs AFTER serialization wait. If placed before, an update can
///     pass the drain-window check, wait at the serialization gate while <c>DeactivateAsync</c>
///     runs, then execute after <c>_deactivating = true</c> — defeating the gate entirely.
///   </description></item>
///   <item><description>
///     <b>Exception safety net</b> — converts arbitrary non-<c>FailureException</c> exceptions
///     into a non-retryable <c>ApplicationFailureException(errorType: "UnhandledUpdateException")</c>,
///     preventing the workflow task from failing and later updates from stalling until a fixed
///     worker is deployed.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>Admission errors:</b> Queries received before activation completes fail with
/// <c>errorType: "ObjectNotReady"</c> and message
/// <c>Object is not ready: activation is incomplete.</c>. Updates received after
/// Continue-as-New admission closes fail before user code with
/// <c>errorType: "ObjectContinuingAsNew"</c> and message
/// <c>Update '&lt;name&gt;' rejected: object is continuing as new.</c>. This known pre-handler
/// rejection can be retried against the next run; unknown outcomes still require application
/// idempotency.
/// </para>
/// <para>
/// <b>Authorization and reminders:</b> If an <c>authorize</c> predicate is registered and the
/// worker uses canonical-object reminders, the predicate must explicitly allow reminder delivery.
/// Use <see cref="FrameworkUpdateNames"/> to permit framework-internal updates without
/// guessing their names. See <c>docs/failure-handling.md</c> under "Authorization and framework
/// updates" for the recommended predicate pattern.
/// </para>
/// </remarks>
public sealed class DurableObjectWorkerInterceptor : IWorkerInterceptor
{
    private readonly bool _serialize;
    private readonly Func<HandleUpdateInput, bool>? _authorize;
    private readonly Func<HandleSignalInput, bool>? _authorizeSignal;

    /// <summary>
    /// Initializes a new <see cref="DurableObjectWorkerInterceptor"/> with the given settings.
    /// </summary>
    /// <param name="serialize">
    /// When <see langword="true"/> (default), update handlers run strictly turn-based
    /// (non-reentrant). Set to <see langword="false"/> to allow reentrant handlers.
    /// </param>
    /// <param name="authorize">
    /// Optional authorization predicate. Return <see langword="false"/> to reject an update with
    /// <c>errorType: "Unauthorized"</c>. A thrown exception is converted to
    /// <c>errorType: "AuthorizationFailure"</c> without exposing its details to the caller.
    /// <see langword="null"/> allows all updates.
    /// Must be synchronous — async I/O in workflow context is non-deterministic.
    /// </param>
    public DurableObjectWorkerInterceptor(
        bool serialize = true,
        Func<HandleUpdateInput, bool>? authorize = null)
        : this(serialize, authorize, null)
    {
    }

    /// <summary>Creates an interceptor with independent update and signal authorization.</summary>
    /// <param name="serialize">Serialize updates and signals together across awaits.</param>
    /// <param name="authorize">Existing synchronous update authorization.</param>
    /// <param name="authorizeSignal">
    /// Synchronous deterministic signal authorization. Denials and callback failures drop and log.
    /// No external I/O is allowed.
    /// </param>
    public DurableObjectWorkerInterceptor(
        bool serialize,
        Func<HandleUpdateInput, bool>? authorize,
        Func<HandleSignalInput, bool>? authorizeSignal)
    {
        _serialize = serialize;
        _authorize = authorize;
        _authorizeSignal = authorizeSignal;
    }

    /// <summary>
    /// The set of update wire names that the framework delivers internally.
    /// Currently contains only <c>"OnReminder"</c> (delivered by
    /// <c>ReminderDeliveryActivities.DeliverReminderAsync</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>"Deactivate"</c> is NOT included — it is user-initiated and goes through auth like
    /// any other update. Wire names are post-<c>Async</c>-strip (the SDK strips the trailing
    /// <c>"Async"</c> suffix per <c>WorkflowUpdateDefinition.cs:156-161</c>).
    /// </para>
    /// <para>
    /// <b>Security:</b> These names are predictable from library source. Only use
    /// <c>FrameworkUpdateNames.Contains()</c> in the <c>authorize</c> predicate in
    /// single-tenant or mTLS-secured deployments where namespace access is the security
    /// boundary. In multi-tenant deployments, use a shared-secret header approach instead.
    /// See <c>docs/failure-handling.md</c> under "Multi-tenant authorization."
    /// </para>
    /// </remarks>
    public static readonly FrozenSet<string> FrameworkUpdateNames =
        new[] { "OnReminder" }.ToFrozenSet(StringComparer.Ordinal);

    /// <inheritdoc/>
    public WorkflowInboundInterceptor InterceptWorkflow(WorkflowInboundInterceptor nextInterceptor) =>
        new DurableObjectWorkflowInterceptor(nextInterceptor, _serialize, _authorize, _authorizeSignal);

#if !NETCOREAPP3_0_OR_GREATER
    /// <inheritdoc/>
    /// <remarks>Pass-through — this interceptor only instruments workflow inbound calls.</remarks>
    ActivityInboundInterceptor IWorkerInterceptor.InterceptActivity(
        ActivityInboundInterceptor nextInterceptor) =>
        nextInterceptor;

    /// <inheritdoc/>
    /// <remarks>Pass-through — this interceptor only instruments workflow inbound calls.</remarks>
    NexusOperationInboundInterceptor IWorkerInterceptor.InterceptNexusOperation(
        NexusOperationInboundInterceptor nextInterceptor) => nextInterceptor;
#endif

    private sealed class DurableObjectWorkflowInterceptor : WorkflowInboundInterceptor
    {
        private readonly bool _serialize;
        private readonly Func<HandleUpdateInput, bool>? _authorize;
        private readonly Func<HandleSignalInput, bool>? _authorizeSignal;
        private bool _gate; // serialization gate — per-workflow-execution instance

        public DurableObjectWorkflowInterceptor(
            WorkflowInboundInterceptor next,
            bool serialize,
            Func<HandleUpdateInput, bool>? authorize,
            Func<HandleSignalInput, bool>? authorizeSignal)
            : base(next)
        {
            _serialize = serialize;
            _authorize = authorize;
            _authorizeSignal = authorizeSignal;
        }

        public override async Task HandleSignalAsync(HandleSignalInput input)
        {
            if (Workflow.Instance is not DurableObjectBase instance)
            {
                await Next.HandleSignalAsync(input).ConfigureAwait(true);
                return;
            }

            // Startup scanning can be bypassed by manual interceptors, late registration,
            // or Workflow.DynamicSignal assignment. Check the actual SDK-selected handler.
            if (input.Definition.Dynamic)
            {
                ReportSignalDrop(input.Signal, "DynamicSignalNotSupported", LogLevel.Error);
                return;
            }

            if (_authorize is not null && _authorizeSignal is null)
            {
                ReportSignalDrop(input.Signal, "SignalAuthorizationNotConfigured", LogLevel.Error);
                return;
            }

            if (!instance.IsActivated)
            {
                await Workflow.WaitConditionAsync(() => instance.IsActivated).ConfigureAwait(true);
            }

            if (instance.IsSignalAdmissionClosed)
            {
                ReportSignalDrop(input.Signal, "ObjectDeactivating", LogLevel.Warning);
                return;
            }

            bool authorized;
            try
            {
                authorized = _authorizeSignal?.Invoke(input) ?? true;
            }
#pragma warning disable CA1031 // Signal callback and handler failures must not wedge the object
            catch (Exception ex) when (ShouldContainSignalException(ex))
            {
                ReportSignalDrop(input.Signal, "AuthorizationFailure", LogLevel.Error, ex.GetType().FullName);
                return;
            }
#pragma warning restore CA1031

            if (!authorized)
            {
                ReportSignalDrop(input.Signal, "Unauthorized", LogLevel.Warning);
                return;
            }

            if (_serialize)
            {
                while (_gate)
                {
                    await Workflow.WaitConditionAsync(() => !_gate).ConfigureAwait(true);
                }

                _gate = true;
            }

            try
            {
                if (instance.IsSignalAdmissionClosed)
                {
                    ReportSignalDrop(input.Signal, "ObjectDeactivating", LogLevel.Warning);
                    return;
                }

                // Accepted signals cannot be rejected during rollover. A new admission during
                // an asynchronous snapshot requires draining and taking another snapshot.
                instance.SignalAdmitted();
                try
                {
                    await Next.HandleSignalAsync(input).ConfigureAwait(true);
                }
#pragma warning disable CA1031 // Intentional signal exception containment, without rollback
                catch (Exception ex) when (ShouldContainSignalException(ex))
                {
                    ReportSignalDrop(input.Signal, "SignalHandlerFailure", LogLevel.Error, ex.GetType().FullName);
                }
#pragma warning restore CA1031
            }
            finally
            {
                if (_serialize)
                {
                    _gate = false;
                }
            }
        }

        private static bool ShouldContainSignalException(Exception exception) =>
            exception is not ContinueAsNewException &&
            !(exception is OperationCanceledException && Workflow.CancellationToken.IsCancellationRequested);

        private static void ReportSignalDrop(
            string signal, string category, LogLevel level, string? errorType = null)
        {
            // Only identifiers and type names: never attach exception objects, messages,
            // payloads or headers. Workflow.Logger suppresses ordinary replay duplicates.
#pragma warning disable CA1031 // A reporting failure must not defeat signal containment
#pragma warning disable CA1848 // One stable structured event, not a hot-path diagnostic
            try
            {
                var logger = Workflow.Logger;
                if (!logger.IsEnabled(level))
                {
                    return;
                }

                logger.Log(
                    level,
                    new EventId(4101, "DurableObjectSignalDropped"),
                    "DurableObject signal dropped: {Category}; Signal={Signal}; ErrorType={ErrorType}",
                    category, signal, errorType);
            }
            catch (Exception)
            {
                // Best effort only. Never recursively report a broken logging provider.
            }
#pragma warning restore CA1848
#pragma warning restore CA1031
        }

        public override async Task<object?> HandleUpdateAsync(HandleUpdateInput input)
        {
            var instance = Workflow.Instance as DurableObjectBase;
            // Update-with-start can enter before RunAsync registers the object. Use the
            // execution-local instance and wait before authorization, serialization, or user code.
            if (instance is not null && !instance.IsActivated)
            {
                await Workflow.WaitConditionAsync(() => instance.IsActivated).ConfigureAwait(true);
            }

            // Once rollover admission closes, return the stable retryable-by-caller rejection
            // before invoking application authorization code.
            if (instance is not null && instance.IsContinuingAsNew)
            {
                ThrowObjectContinuingAsNew(input.Update);
            }

            // Authorization runs before the serialization gate so rejected updates don't consume it.
            // NOTE: auth runs here (in HandleUpdateAsync), NOT in ValidateUpdate. The SDK only
            // invokes the interceptor's ValidateUpdate when [WorkflowUpdateValidator] is declared
            // (WorkflowInstance.cs:1206 gates on ValidatorMethod != null), so a validation-phase
            // check would silently skip plain updates. Throwing here — before Next — rejects the
            // update cleanly: the caller sees WorkflowUpdateFailedException, the object stays alive.
            bool authorized;
            try
            {
                authorized = _authorize?.Invoke(input) ?? true;
            }
#pragma warning disable CA1031 // Authorization callback exceptions must not fail the workflow task
            catch (Exception ex)
            {
#pragma warning disable CA1848 // Use LoggerMessage delegates — acceptable in interceptor; not a hot path
                Workflow.Logger.LogError(
                    ex,
                    "Authorization callback failed for update '{Update}'",
                    input.Update);
#pragma warning restore CA1848
                throw new ApplicationFailureException(
                    $"Update '{input.Update}' rejected: authorization callback failed.",
                    errorType: "AuthorizationFailure",
                    nonRetryable: true);
            }
#pragma warning restore CA1031

            if (!authorized)
            {
                throw new ApplicationFailureException(
                    $"Update '{input.Update}' rejected: unauthorized.",
                    errorType: "Unauthorized",
                    nonRetryable: true);
            }

            // Serialization gate.
            // while-loop (not if) — correct when WaitConditionAsync releases multiple waiters at
            // once (the scheduler is single-threaded so check-then-set is atomic, but multiple
            // waiters can wake on the same condition pass). See WorkflowInstance.cs:897-908.
            if (_serialize)
            {
                while (_gate)
                {
                    await Workflow.WaitConditionAsync(() => !_gate).ConfigureAwait(true);
                }

                _gate = true;
            }

            try
            {
                // Lifecycle gates. Rollover/deactivation may have started.
                // MUST come after the serialization wait (see class-level remarks for the
                // ordering rationale — checking before the gate allows a window where an update
                // passes the drain check, waits, then executes after _deactivating is set).
                if (instance is not null)
                {
                    if (instance.IsContinuingAsNew)
                    {
                        ThrowObjectContinuingAsNew(input.Update);
                    }

                    if (instance.IsDeactivating)
                    {
                        throw new ApplicationFailureException(
                            $"Update '{input.Update}' rejected: object is deactivating.",
                            errorType: "ObjectDeactivating",
                            nonRetryable: true);
                    }
                }

                // Exception safety net.
                // Arbitrary exceptions from update handlers set currentActivationException in the
                // SDK (WorkflowInstance.cs:1307, 1319), failing the workflow task. The server retries
                // it, so this and later updates stall until a fixed worker is deployed. Convert any
                // such exception into a clean
                // non-retryable ApplicationFailureException so the caller sees
                // WorkflowUpdateFailedException and the object stays alive.
                // FailureException and OperationCanceledException propagate unchanged.
                try
                {
                    return await Next.HandleUpdateAsync(input).ConfigureAwait(true);
                }
#pragma warning disable CA1031 // Do not catch general exception types — intentional safety net
                catch (Exception ex) when (ex is not FailureException && ex is not OperationCanceledException)
                {
#pragma warning disable CA1848 // Use LoggerMessage delegates — acceptable in interceptor; not a hot path
                    Workflow.Logger.LogError(
                        ex,
                        "Unhandled exception in update handler '{Update}'",
                        input.Update);
#pragma warning restore CA1848
                    throw new ApplicationFailureException(
                        $"Update '{input.Update}' failed with an unhandled exception: {ex.Message}",
                        ex,
                        errorType: "UnhandledUpdateException",
                        nonRetryable: true);
                }
#pragma warning restore CA1031
            }
            finally
            {
                if (_serialize)
                {
                    _gate = false;
                }
            }
        }

        public override object? HandleQuery(HandleQueryInput input)
        {
            if (Workflow.Instance is DurableObjectBase instance && !instance.IsActivated)
            {
                throw new ApplicationFailureException(
                    "Object is not ready: activation is incomplete.",
                    errorType: "ObjectNotReady",
                    nonRetryable: true);
            }

            return Next.HandleQuery(input);
        }

        private static void ThrowObjectContinuingAsNew(string update) =>
            throw new ApplicationFailureException(
                $"Update '{update}' rejected: object is continuing as new.",
                errorType: "ObjectContinuingAsNew",
                nonRetryable: true);
    }
}
