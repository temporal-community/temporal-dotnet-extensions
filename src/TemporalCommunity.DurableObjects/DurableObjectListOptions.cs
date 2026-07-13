namespace TemporalCommunity.DurableObjects;

/// <summary>Immutable options for enumerating DurableObject executions through visibility.</summary>
public sealed class DurableObjectListOptions
{
    /// <summary>Creates visibility listing options.</summary>
    /// <param name="runningOnly">Whether to return only open executions.</param>
    /// <param name="includeScheduled">
    /// Whether to include executions created by Temporal Schedules alongside canonical objects.
    /// </param>
    public DurableObjectListOptions(bool runningOnly = true, bool includeScheduled = false)
    {
        RunningOnly = runningOnly;
        IncludeScheduled = includeScheduled;
    }

    /// <summary>Gets whether only open executions are returned.</summary>
    public bool RunningOnly { get; }

    /// <summary>Gets whether schedule-created executions are included.</summary>
    public bool IncludeScheduled { get; }
}
