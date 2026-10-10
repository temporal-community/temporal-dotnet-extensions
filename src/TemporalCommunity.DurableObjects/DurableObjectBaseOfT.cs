namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Base class for a DurableObject with one strongly typed state model that is carried through
/// Continue-as-New automatically.
/// </summary>
/// <typeparam name="TState">The object's durable state type.</typeparam>
/// <remarks>
/// The concrete workflow declares a <c>[WorkflowInit]</c> constructor that accepts an optional
/// <see cref="DurableObjectSnapshot{TState}"/> and passes it with a cold-start value to this base.
/// Continue-as-New supplies the snapshot created from <see cref="State"/>.
/// </remarks>
public abstract class DurableObjectBase<TState> : DurableObjectBase
    where TState : notnull
{
    /// <summary>Initializes an object with restored or cold-start state.</summary>
    /// <param name="snapshot">State from the prior execution, or null on a cold start.</param>
    /// <param name="initialState">State to use on a cold start.</param>
    protected DurableObjectBase(
        DurableObjectSnapshot<TState>? snapshot,
        TState initialState)
    {
        State = GetState(snapshot, initialState);
    }

    /// <summary>Initializes an object with restored or cold-start state and runtime options.</summary>
    /// <param name="snapshot">State from the prior execution, or null on a cold start.</param>
    /// <param name="initialState">State to use on a cold start.</param>
    /// <param name="options">Run-loop scheduling configuration.</param>
    protected DurableObjectBase(
        DurableObjectSnapshot<TState>? snapshot,
        TState initialState,
        DurableObjectOptions options)
        : base(options)
    {
        State = GetState(snapshot, initialState);
    }

    /// <summary>Gets or sets the current durable state.</summary>
    protected TState State { get; set; }

    /// <summary>
    /// Allows the object to normalize or migrate its state immediately before Continue-as-New.
    /// May run more than once when signals enter user code during asynchronous preparation.
    /// Implementations and any external effects must be idempotent.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>The state to carry into the next execution.</returns>
    protected virtual Task<TState> PrepareStateForContinueAsNewAsync(TState state) =>
        Task.FromResult(state);

    /// <inheritdoc/>
    protected sealed override async Task<IReadOnlyCollection<object?>> OnBeforeContinueAsNewAsync()
    {
        var state = await PrepareStateForContinueAsNewAsync(State).ConfigureAwait(true);
        if (state is null)
        {
            throw new InvalidOperationException(
                $"{nameof(PrepareStateForContinueAsNewAsync)} must return non-null state for {GetType().Name}.");
        }

        return new object?[] { new DurableObjectSnapshot<TState>(state) };
    }

    private static TState GetState(
        DurableObjectSnapshot<TState>? snapshot,
        TState initialState)
    {
        var state = snapshot is null ? initialState : snapshot.State;
        return state is null
            ? throw new ArgumentNullException(nameof(initialState), "DurableObject state cannot be null.")
            : state;
    }
}
