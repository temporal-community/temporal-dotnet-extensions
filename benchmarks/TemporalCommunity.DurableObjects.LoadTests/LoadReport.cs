namespace TemporalCommunity.DurableObjects.LoadTests;

public sealed record CallSample(long Sequence, int ObjectIndex, double StartedSeconds,
    double CompletedSeconds, double LatencyMs, string? Error);

public sealed record ObservationSample(double ElapsedSeconds, int ObjectIndex,
    CounterObservation Observation, int OutstandingCalls, int ThreadPoolThreads,
    long ThreadPoolPendingWorkItems, long WorkingSetBytes);

public sealed record LatencySummary(double P50Ms, double P95Ms, double P99Ms, double MaxMs)
{
    public static LatencySummary From(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        double Percentile(double fraction) => sorted.Length == 0
            ? 0 : sorted[(int)Math.Ceiling(fraction * sorted.Length) - 1];
        return new(Percentile(.5), Percentile(.95), Percentile(.99), sorted.Length == 0 ? 0 : sorted[^1]);
    }
}

public sealed record LoadSummary(
    long Offered, long Started, long Completed, long Failed, long CapacityMisses, long SchedulerMisses,
    int MaxOutstandingCalls, bool OperationLimitReached,
    double OfferingSeconds, double TotalSeconds,
    double CompletedPerSecondDuringOffering, double CompletedPerSecondIncludingDrain,
    LatencySummary SuccessfulCallLatency, LatencySummary FailedCallLatency,
    int RolloversObservedDuringOffering, int FinalRollovers,
    int MaxSampledHistoryLength, long MaxSampledHistoryBytes, double MaxSampledSecondsAboveHistoryThreshold,
    long SnapshotPayloadBytesAcrossObjects);

public sealed record LoadReport(string RunId, DateTimeOffset CreatedAt, string Runtime,
    string OperatingSystem, string LibraryVersion, string ServerMode, string TaskQueue,
    LoadOptions Options, LoadSummary Summary, IReadOnlyList<string> Failures,
    IReadOnlyList<string> MonitoringErrors, IReadOnlyList<CounterObservation> FinalObjects,
    IReadOnlyList<CallSample> Calls, IReadOnlyList<ObservationSample> Observations);
