namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Abstract base class for client-visible DurableObject errors. All exceptions that a
/// <c>DurableObjectProxy&lt;T&gt;</c> maps from SDK-level errors and surfaces to callers
/// extend this class.
/// </summary>
/// <remarks>
/// <para>
/// Only errors the <em>client</em> can meaningfully distinguish and react to are represented
/// in this hierarchy:
/// <list type="bullet">
///   <item><see cref="DurableObjectNotFoundException"/> — the object's workflow history does not exist.</item>
///   <item><see cref="DurableObjectNotActiveException"/> — the object exists but is closed or deactivated.</item>
/// </list>
/// </para>
/// <para>
/// Lifecycle hook failures (<c>OnActivateAsync</c>, <c>OnTimerAsync</c>,
/// <c>OnBeforeContinueAsNewAsync</c>) are NOT represented here. The framework throws
/// <c>ApplicationFailureException</c> directly (non-retryable) to terminate the workflow
/// cleanly rather than wedging it in an infinite task-retry loop. See
/// <c>docs/FAILURE_HANDLING.md</c> for the full failure taxonomy.
/// </para>
/// <para>
/// <c>WorkflowUpdateFailedException</c> is intentionally NOT mapped — it carries the
/// application's own <c>ApplicationFailureException</c> from inside an update handler and is
/// meaningful to callers. It is re-thrown unchanged so callers can inspect
/// <c>inner.ApplicationFailureException.ErrorType</c> for domain-specific error codes.
/// </para>
/// </remarks>
public abstract class DurableObjectException : Exception
{
    /// <summary>Initializes a new instance with a default message.</summary>
    protected DurableObjectException() : base("A DurableObject error occurred.") { }

    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The exception message describing the error.</param>
    protected DurableObjectException(string message) : base(message) { }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    /// <param name="message">The exception message describing the error.</param>
    /// <param name="innerException">The underlying SDK exception that caused this error.</param>
    protected DurableObjectException(string message, Exception innerException)
        : base(message, innerException) { }
}
