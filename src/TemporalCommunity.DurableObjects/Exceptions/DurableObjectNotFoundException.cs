namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Thrown by a <c>DurableObjectProxy&lt;T&gt;</c> when a query targets a DurableObject that does
/// not exist — either because it was never created or because its workflow history has been purged.
/// </summary>
/// <remarks>
/// Queries are read-only and must never materialize (start) an object as a side effect. Unlike
/// updates (which use update-with-start and atomically activate the object on first contact),
/// the query path does not auto-activate. When the target workflow execution is not found, the
/// proxy surfaces this exception instead of silently returning a default value.
/// </remarks>
public sealed class DurableObjectNotFoundException : DurableObjectException
{
    /// <summary>Initializes a new instance with a default message.</summary>
    public DurableObjectNotFoundException()
        : base("The specified durable object does not exist.")
        => ObjectId = string.Empty;

    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The exception message.</param>
    public DurableObjectNotFoundException(string message)
        : base(message)
        => ObjectId = string.Empty;

    /// <summary>
    /// Initializes a new instance identifying the missing object by its workflow ID.
    /// </summary>
    /// <param name="objectId">
    /// The workflow ID of the DurableObject that was not found.
    /// </param>
    /// <param name="innerException">
    /// The underlying SDK or RPC exception that triggered this mapping. Pass <c>null</c> when
    /// there is no underlying exception to associate.
    /// </param>
    public DurableObjectNotFoundException(string objectId, Exception? innerException)
        : base(
            $"Durable object '{objectId}' does not exist. Queries do not create objects; " +
            "invoke an update first to activate it.",
            innerException!)
        => ObjectId = objectId;

    /// <summary>Gets the workflow ID of the DurableObject that was not found.</summary>
    public string ObjectId { get; }
}
