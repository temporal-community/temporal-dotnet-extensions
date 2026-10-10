using System.Collections.ObjectModel;
using Temporalio.Client;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Per-call transport options for DurableObject queries, updates, and signals.
/// </summary>
/// <remarks>
/// These options affect only the client RPC. Cancelling an RPC does not cancel an update that the
/// Temporal server has already accepted, or retract a recorded signal. Values are snapshotted when this instance is constructed,
/// so later changes to caller-owned dictionaries or byte arrays are not observed.
/// </remarks>
public sealed class DurableObjectCallOptions
{
    private readonly IReadOnlyDictionary<string, string>? _metadata;
    private readonly IReadOnlyDictionary<string, byte[]>? _binaryMetadata;

    /// <summary>Creates a set of immutable per-call transport options.</summary>
    /// <param name="rpcTimeout">Optional timeout for the client RPC.</param>
    /// <param name="retry">Whether the Temporal client should retry the RPC.</param>
    /// <param name="metadata">Optional gRPC text metadata, such as authorization or trace headers.</param>
    /// <param name="binaryMetadata">Optional gRPC binary metadata.</param>
    /// <param name="cancellationToken">Token that cancels waiting for the client RPC.</param>
    public DurableObjectCallOptions(
        TimeSpan? rpcTimeout = null,
        bool? retry = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyDictionary<string, byte[]>? binaryMetadata = null,
        CancellationToken cancellationToken = default)
    {
        CancellationToken = cancellationToken;
        RpcTimeout = rpcTimeout;
        Retry = retry;
        _metadata = CopyMetadata(metadata);
        _binaryMetadata = CopyBinaryMetadata(binaryMetadata);
    }

    /// <summary>Gets the token that cancels waiting for the client RPC.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Gets the optional client RPC timeout.</summary>
    public TimeSpan? RpcTimeout { get; }

    /// <summary>Gets whether the Temporal client should retry the RPC.</summary>
    public bool? Retry { get; }

    /// <summary>Gets the snapshotted gRPC text metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata => _metadata;

    /// <summary>Gets the snapshotted gRPC binary metadata.</summary>
    public IReadOnlyDictionary<string, byte[]>? BinaryMetadata => CopyBinaryMetadata(_binaryMetadata);

    internal RpcOptions ToRpcOptions() => new()
    {
        CancellationToken = CancellationToken,
        Timeout = RpcTimeout,
        Retry = Retry,
        Metadata = _metadata is null ? null : new Dictionary<string, string>(_metadata),
        BinaryMetadata = _binaryMetadata is null
            ? null
            : _binaryMetadata.ToDictionary(pair => pair.Key, pair => (byte[])pair.Value.Clone()),
    };

    private static ReadOnlyDictionary<string, string>? CopyMetadata(
        IReadOnlyDictionary<string, string>? metadata) =>
        metadata is null
            ? null
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(metadata));

    private static ReadOnlyDictionary<string, byte[]>? CopyBinaryMetadata(
        IReadOnlyDictionary<string, byte[]>? metadata) =>
        metadata is null
            ? null
            : new ReadOnlyDictionary<string, byte[]>(
                metadata.ToDictionary(pair => pair.Key, pair => (byte[])pair.Value.Clone()));
}
