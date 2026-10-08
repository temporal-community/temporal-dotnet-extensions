using Temporalio.Worker.Interceptors;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Options for DurableObject worker registration and interceptor behavior. Pass to
/// <see cref="DurableObjectWorkerExtensions.AddDurableObjectWorkflows(Temporalio.Extensions.Hosting.ITemporalWorkerServiceOptionsBuilder, System.Reflection.Assembly, DurableObjectWorkerOptions?)"/>
/// (or the <see cref="Temporalio.Worker.TemporalWorkerOptions"/> overload) to customize the
/// automatically-installed <see cref="DurableObjectWorkerInterceptor"/>.
/// </summary>
public sealed record DurableObjectWorkerOptions
{
    /// <summary>
    /// When <see langword="true"/> (default), update handlers run strictly turn-based
    /// (non-reentrant): while one handler is between awaits, subsequent updates wait.
    /// Mirrors Orleans grain behavior. Set to <see langword="false"/> only if you need
    /// reentrant update handlers and are managing concurrency yourself.
    /// </summary>
    public bool Serialize { get; init; } = true;

    /// <summary>
    /// Optional authorization predicate. When set, called for every inbound update before the
    /// handler runs. Return <see langword="false"/> to reject with
    /// <c>errorType: "Unauthorized"</c>. If the predicate throws, the update is rejected with
    /// <c>errorType: "AuthorizationFailure"</c> without exposing the callback exception to the
    /// caller. When <see langword="null"/>, all updates are allowed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Sync-only constraint:</b> This predicate runs inside the workflow task scheduler.
    /// Async I/O (JWT validation, external auth service calls) is non-deterministic in that
    /// context and will cause replay divergence. Token or header auth must be performed
    /// synchronously from the data already present in <see cref="HandleUpdateInput"/>.
    /// </para>
    /// <para>
    /// <b>Framework updates and reminders:</b> If you register an <c>authorize</c> predicate
    /// AND use canonical-object reminders, the predicate must explicitly permit reminder
    /// delivery — the framework does not auto-bypass auth for its own updates. Use
    /// <see cref="DurableObjectWorkerInterceptor.FrameworkUpdateNames"/> to identify updates
    /// delivered by the framework internally:
    /// <code>
    /// new DurableObjectWorkerOptions
    /// {
    ///     Authorize = input =>
    ///         DurableObjectWorkerInterceptor.FrameworkUpdateNames.Contains(input.Update)
    ///         || MyUserAuthCheck(input)
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// Named delegate type deferred to v1.1 for readability;
    /// <see cref="Func{T, TResult}"/> is correct and interoperable for v1.
    /// </para>
    /// </remarks>
    public Func<HandleUpdateInput, bool>? Authorize { get; init; }
}
