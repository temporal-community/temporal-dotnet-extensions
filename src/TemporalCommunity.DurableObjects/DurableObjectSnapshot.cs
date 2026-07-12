namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Carries a DurableObject's typed state between workflow executions when the object continues
/// as new.
/// </summary>
/// <typeparam name="TState">The object's durable state type.</typeparam>
/// <param name="State">The state restored by the next workflow execution.</param>
public sealed record DurableObjectSnapshot<TState>(TState State)
    where TState : notnull;

