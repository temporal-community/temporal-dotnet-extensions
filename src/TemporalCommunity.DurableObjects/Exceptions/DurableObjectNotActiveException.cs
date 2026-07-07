using Temporalio.Api.Enums.V1;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Thrown by a <c>DurableObjectProxy&lt;T&gt;</c> when a query targets a DurableObject that
/// exists but is no longer open — for example, because it was explicitly deactivated or its
/// workflow completed for another reason.
/// </summary>
/// <remarks>
/// Queries use <c>QueryRejectCondition.NotOpen</c> so that a closed object surfaces this
/// exception rather than silently returning its stale final state. This makes a deactivated
/// object distinguishable from an active one that happens to hold a default value.
/// To query the object again it must first be re-activated by invoking an update.
/// </remarks>
public sealed class DurableObjectNotActiveException : DurableObjectException
{
    /// <summary>Initializes a new instance with a default message.</summary>
    public DurableObjectNotActiveException()
        : base("The specified durable object is not active.")
    {
        ObjectId = string.Empty;
        Status = WorkflowExecutionStatus.Completed;
    }

    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The exception message.</param>
    public DurableObjectNotActiveException(string message)
        : base(message)
    {
        ObjectId = string.Empty;
        Status = WorkflowExecutionStatus.Completed;
    }

    /// <summary>Initializes a new instance with the specified message and inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The underlying exception that caused this error.</param>
    public DurableObjectNotActiveException(string message, Exception innerException)
        : base(message, innerException)
    {
        ObjectId = string.Empty;
        Status = WorkflowExecutionStatus.Completed;
    }

    /// <summary>
    /// Initializes a new instance identifying the inactive object and its workflow status.
    /// </summary>
    /// <param name="objectId">
    /// The workflow ID of the DurableObject that is not active.
    /// </param>
    /// <param name="status">
    /// The <see cref="WorkflowExecutionStatus"/> of the closed workflow execution.
    /// </param>
    /// <param name="innerException">
    /// The underlying SDK or RPC exception that triggered this mapping, if any.
    /// </param>
    public DurableObjectNotActiveException(
        string objectId,
        WorkflowExecutionStatus status,
        Exception? innerException = null)
        : base(
            $"Durable object '{objectId}' is not active (status: {status}). It was deactivated " +
            "or otherwise closed; invoke an update to re-activate it before querying.",
            innerException!)
    {
        ObjectId = objectId;
        Status = status;
    }

    /// <summary>Gets the workflow ID of the DurableObject that is not active.</summary>
    public string ObjectId { get; }

    /// <summary>Gets the workflow execution status of the closed execution.</summary>
    public WorkflowExecutionStatus Status { get; }
}
