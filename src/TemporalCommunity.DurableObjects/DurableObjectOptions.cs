namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Construction-time configuration for a <see cref="DurableObjectBase"/>.
/// Pass an instance to the <see cref="DurableObjectBase(DurableObjectOptions)"/> constructor
/// to customize run-loop scheduling behavior. Subclasses typically construct and pass this record
/// rather than exposing it directly to callers.
/// </summary>
/// <remarks>
/// <para>
/// <c>DurableObjectOptions</c> governs only the object's run-loop scheduling behavior.
/// Task queue, namespace, and other worker/client concerns are not represented here.
/// </para>
/// <para>
/// All properties are validated at construction time using the <c>init</c> setter pattern —
/// invalid values throw <see cref="ArgumentOutOfRangeException"/> immediately, so a
/// misconfigured options object never reaches the run loop.
/// </para>
/// </remarks>
public sealed record DurableObjectOptions
{
    private int _maxHistoryLength = 10_000;

    /// <summary>
    /// History event count that triggers a ContinueAsNew to shed accumulated workflow history.
    /// The run loop checks this on every iteration; when <c>Workflow.Info.HistoryLength</c>
    /// reaches this value, the loop drains in-flight handlers, calls
    /// <see cref="DurableObjectBase.OnBeforeContinueAsNewAsync"/>, then issues a
    /// ContinueAsNew to start a fresh execution with the same workflow ID.
    /// </summary>
    /// <value>
    /// A positive integer. Default is <c>10,000</c> events — well below the Temporal server
    /// ceiling (50,000 by default) to leave headroom for burst activity between checks.
    /// </value>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown at construction time if the value is zero or negative.
    /// </exception>
    public int MaxHistoryLength
    {
        get => _maxHistoryLength;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxHistoryLength = value;
        }
    }
}
