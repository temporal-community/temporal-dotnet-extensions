using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Temporalio.Exceptions;
using Temporalio.Worker.Interceptors;
using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Worker interceptor that installs cross-cutting concerns on every DurableObject update handler:
/// authorization, serialization (non-reentrant by default), drain-window gating, and an
/// exception safety net. Installed automatically by
/// <see cref="DurableObjectWorkerExtensions.AddDurableObjectWorkflows(Temporalio.Extensions.Hosting.ITemporalWorkerServiceOptionsBuilder, System.Reflection.Assembly, DurableObjectWorkerOptions?)"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Five responsibilities (order matters inside <c>HandleUpdateAsync</c>):</b>
/// <list type="number">
///   <item><description>
///     <b>Authorization</b> — runs first so unauthorized updates never consume the serialization gate.
///   </description></item>
///   <item><description>
///     <b>Serialization gate</b> — while one handler is between awaits, subsequent updates wait.
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
///     preventing the workflow task from failing and the object from wedging permanently.
///   </description></item>
/// </list>
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

    /// <summary>
    /// Initializes a new <see cref="DurableObjectWorkerInterceptor"/> with the given settings.
    /// </summary>
    /// <param name="serialize">
    /// When <see langword="true"/> (default), update handlers run strictly turn-based
    /// (non-reentrant). Set to <see langword="false"/> to allow reentrant handlers.
    /// </param>
    /// <param name="authorize">
    /// Optional authorization predicate. Return <see langword="false"/> to reject an update with
    /// <c>errorType: "Unauthorized"</c>. <see langword="null"/> allows all updates.
    /// Must be synchronous — async I/O in workflow context is non-deterministic.
    /// </param>
    public DurableObjectWorkerInterceptor(
        bool serialize = true,
        Func<HandleUpdateInput, bool>? authorize = null)
    {
        _serialize = serialize;
        _authorize = authorize;
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
        new DurableObjectWorkflowInterceptor(nextInterceptor, _serialize, _authorize);

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
        private bool _gate; // serialization gate — per-workflow-execution instance

        public DurableObjectWorkflowInterceptor(
            WorkflowInboundInterceptor next,
            bool serialize,
            Func<HandleUpdateInput, bool>? authorize)
            : base(next)
        {
            _serialize = serialize;
            _authorize = authorize;
        }

        public override async Task<object?> HandleUpdateAsync(HandleUpdateInput input)
        {
            // Responsibility 1: Authorization.
            // Runs before the serialization gate so unauthorized updates don't consume it.
            // NOTE: auth runs here (in HandleUpdateAsync), NOT in ValidateUpdate. The SDK only
            // invokes the interceptor's ValidateUpdate when [WorkflowUpdateValidator] is declared
            // (WorkflowInstance.cs:1206 gates on ValidatorMethod != null), so a validation-phase
            // check would silently skip plain updates. Throwing here — before Next — rejects the
            // update cleanly: the caller sees WorkflowUpdateFailedException, the object stays alive.
            if (_authorize != null && !_authorize(input))
            {
                throw new ApplicationFailureException(
                    $"Update '{input.Update}' rejected: unauthorized.",
                    errorType: "Unauthorized",
                    nonRetryable: true);
            }

            // Responsibility 2: Serialization gate.
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
                // Responsibility 3: Drain-window gate.
                // MUST come after the serialization wait (see class-level remarks for the
                // ordering rationale — checking before the gate allows a window where an update
                // passes the drain check, waits, then executes after _deactivating is set).
                if (DurableObjectBase.TryGetCurrent(Workflow.Info.WorkflowId, out var instance)
                    && instance.IsDeactivating)
                {
                    throw new ApplicationFailureException(
                        $"Update '{input.Update}' rejected: object is deactivating.",
                        errorType: "ObjectDeactivating",
                        nonRetryable: true);
                }

                // Responsibility 4: Exception safety net.
                // Arbitrary exceptions from update handlers set currentActivationException in the
                // SDK (WorkflowInstance.cs:1307, 1319), retrying the workflow task indefinitely
                // and permanently wedging the object. Convert any such exception into a clean
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
    }
}
