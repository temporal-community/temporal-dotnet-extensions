using System.Globalization;

namespace TemporalCommunity.DurableObjects.LoadTests;

public sealed record LoadOptions
{
    public int DurationSeconds { get; init; } = 10;
    public int Concurrency { get; init; } = 16;
    public int Objects { get; init; } = 1;
    public int ActivityDelayMs { get; init; } = 25;
    public int HistoryThreshold { get; init; } = 100;
    public int PayloadBytes { get; init; }
    public int ArrivalRate { get; init; }
    public int MaxOperations { get; init; } = 100_000;
    public int RpcTimeoutSeconds { get; init; } = 30;
    public int DrainSeconds { get; init; } = 30;
    public bool Serialize { get; init; } = true;
    public string Operation { get; init; } = "update";
    public string? Address { get; init; }
    public string Namespace { get; init; } = "default";
    public string? Output { get; init; }
    public bool RequireRollover { get; init; }
    public double? P95LimitMs { get; init; }
    public double? MinThroughput { get; init; }

    public static LoadOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var requireRollover = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--require-rollover")
            {
                requireRollover = true;
                continue;
            }

            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 == args.Length ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Expected --option value, received '{args[i]}'. Use --help.");
            }

            values.Add(args[i], args[++i]);
        }

        string? Take(string name) => values.Remove(name, out var value) ? value : null;
        int Integer(string name, int fallback, int min, int max)
        {
            var text = Take(name);
            var value = text is null ? fallback : int.Parse(text, CultureInfo.InvariantCulture);
            if (value < min || value > max)
            {
                throw new ArgumentException($"{name} must be between {min} and {max}.");
            }

            return value;
        }

        double? PositiveNumber(string name)
        {
            var text = Take(name);
            if (text is null) return null;
            var value = double.Parse(text, CultureInfo.InvariantCulture);
            if (!double.IsFinite(value) || value <= 0) throw new ArgumentException($"{name} must be positive.");
            return value;
        }

        var options = new LoadOptions
        {
            DurationSeconds = Integer("--duration-seconds", 10, 1, 3600),
            Concurrency = Integer("--concurrency", 16, 1, 10_000),
            Objects = Integer("--objects", 1, 1, 10_000),
            ActivityDelayMs = Integer("--activity-delay-ms", 25, 0, 10_000),
            HistoryThreshold = Integer("--history-threshold", 100, 30, 10_000),
            PayloadBytes = Integer("--payload-bytes", 0, 0, 1_000_000),
            ArrivalRate = Integer("--arrival-rate", 0, 0, 100_000),
            MaxOperations = Integer("--max-operations", 100_000, 1, 1_000_000),
            RpcTimeoutSeconds = Integer("--rpc-timeout-seconds", 30, 1, 300),
            DrainSeconds = Integer("--drain-seconds", 30, 1, 300),
            Serialize = bool.Parse(Take("--serialize") ?? "true"),
            Operation = Take("--operation") ?? "update",
            Address = Take("--address"),
            Namespace = Take("--namespace") ?? "default",
            Output = Take("--output"),
            RequireRollover = requireRollover,
            P95LimitMs = PositiveNumber("--p95-limit-ms"),
            MinThroughput = PositiveNumber("--min-throughput"),
        };
        if (values.Count != 0) throw new ArgumentException($"Unknown options: {string.Join(", ", values.Keys)}");
        if (options.Operation is not ("update" or "query-async" or "query-sync"))
        {
            throw new ArgumentException("--operation must be update, query-async, or query-sync.");
        }

        if (options.RequireRollover && options.Operation != "update")
        {
            throw new ArgumentException("--require-rollover requires update operations.");
        }

        if (options.Address is null && options.Namespace != "default")
        {
            throw new ArgumentException("--namespace requires --address; the embedded server uses the default namespace.");
        }

        return options;
    }

    public const string Help = """
        Temporal DurableObjects load runner (opt-in, .NET 10, Release configuration)
        Defaults: 10 seconds, 16 callers, 1 object, serialized updates, 25ms activity, threshold 100.
        --duration-seconds N       Offering window; outstanding calls then drain
        --concurrency N            Caller lanes (closed loop), or maximum outstanding calls (fixed rate)
        --objects N                Stable object IDs, addressed round robin
        --arrival-rate N           Offered calls/second; 0 = closed loop; capacity misses are reported
        --operation NAME           update | query-async | query-sync
        --serialize BOOL           true | false (workload handler is safe to overlap)
        --activity-delay-ms N      Activity delay per update; 0 skips activity
        --history-threshold N      Continue-as-New event threshold (30..10000)
        --payload-bytes N          ASCII payload retained in carried state (0..1000000)
        --max-operations N         Cap offered calls and report memory (default 100000)
        --rpc-timeout-seconds N    Per-call RPC timeout (default 30)
        --drain-seconds N          Final state settlement timeout (default 30)
        --address HOST:PORT        External Temporal server; omitted = embedded real-time dev server
        --namespace NAME           External server namespace (default default)
        --output PATH             JSON report (default artifacts/load-tests/<run>.json)
        --require-rollover        Fail unless rollover occurs during the offering window
        --p95-limit-ms N           Optional latency gate on successful calls, including their drain
        --min-throughput N         Optional gate on calls completed per second in the offering window
        --help                    Print this help
        """;
}
