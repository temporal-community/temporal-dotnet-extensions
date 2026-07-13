using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Client.Schedules;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Internal implementation of <see cref="IDurableObjectFactory"/>. Constructed by
/// <see cref="DurableObjectServiceCollectionExtensions.AddDurableObjects"/> and registered as a
/// singleton in the DI container.
/// </summary>
internal sealed class DurableObjectFactory : IDurableObjectFactory
{
    private readonly ITemporalClient _client;
    private readonly string _defaultTaskQueue;

    internal DurableObjectFactory(ITemporalClient client, string defaultTaskQueue)
    {
        _client = client;
        _defaultTaskQueue = defaultTaskQueue;
    }

    /// <inheritdoc/>
    public T Get<T>(string objectId) where T : IDurableObject =>
        GetClient<T>(objectId, _defaultTaskQueue, null);

    /// <inheritdoc/>
    public T Get<T>(string objectId, string taskQueue) where T : IDurableObject =>
        GetClient<T>(objectId, taskQueue, null);

    /// <inheritdoc/>
    public T Get<T>(string objectId, DurableObjectCallOptions callOptions) where T : IDurableObject =>
        GetClient<T>(objectId, _defaultTaskQueue, callOptions);

    /// <inheritdoc/>
    public T Get<T>(string objectId, string taskQueue, DurableObjectCallOptions callOptions)
        where T : IDurableObject =>
        GetClient<T>(objectId, taskQueue, callOptions);

    /// <inheritdoc/>
    public Task<T> GetOrCreateAsync<T>(string objectId, CancellationToken cancellationToken = default)
        where T : IDurableObject =>
        GetOrCreateAsync<T>(
            objectId,
            _defaultTaskQueue,
            new DurableObjectCallOptions(cancellationToken: cancellationToken));

    /// <inheritdoc/>
    public async Task<T> GetOrCreateAsync<T>(
        string objectId,
        string taskQueue,
        CancellationToken cancellationToken = default)
        where T : IDurableObject =>
        await GetOrCreateAsync<T>(
            objectId,
            taskQueue,
            new DurableObjectCallOptions(cancellationToken: cancellationToken)).ConfigureAwait(false);

    /// <inheritdoc/>
    public Task<T> GetOrCreateAsync<T>(string objectId, DurableObjectCallOptions callOptions)
        where T : IDurableObject =>
        GetOrCreateAsync<T>(objectId, _defaultTaskQueue, callOptions);

    /// <inheritdoc/>
    public async Task<T> GetOrCreateAsync<T>(
        string objectId,
        string taskQueue,
        DurableObjectCallOptions callOptions)
        where T : IDurableObject
    {
        var workflowType = DurableObjectNaming.ResolveWorkflowType(typeof(T));

        // StartWorkflowAsync with UseExisting is a race-free "start-if-not-running" operation.
        // IdReusePolicy.AllowDuplicate is set explicitly — the SDK defaults to AllowDuplicate
        // (WorkflowOptions.cs:77) but that is an undocumented default. Setting it here ensures
        // the behavior is clear and does not silently break if the SDK default ever changes.
        await _client.StartWorkflowAsync(
            workflowType,
            Array.Empty<object?>(),
            new WorkflowOptions(id: objectId, taskQueue: taskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
                IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
                Rpc = callOptions.ToRpcOptions(),
            }).ConfigureAwait(false);

        return GetClient<T>(objectId, taskQueue, callOptions);
    }

    /// <inheritdoc/>
    public Task<TResult> QueryDurableObjectAsync<TResult>(
        string objectId,
        string queryName,
        object?[]? args = null,
        CancellationToken cancellationToken = default) =>
        DurableObjectQuery.ExecuteAsync<TResult>(
            _client,
            objectId,
            queryName,
            args ?? [],
            new DurableObjectCallOptions(cancellationToken: cancellationToken));

    /// <inheritdoc/>
    public Task<TResult> QueryDurableObjectAsync<TResult>(
        string objectId,
        string queryName,
        object?[]? args,
        DurableObjectCallOptions callOptions) =>
        DurableObjectQuery.ExecuteAsync<TResult>(_client, objectId, queryName, args ?? [], callOptions);

    /// <inheritdoc/>
    [return: MaybeNull]
    public Task<TResult> QueryOrDefaultAsync<TResult>(
        string objectId,
        string queryName,
        object?[]? args = null,
        CancellationToken cancellationToken = default)
        => QueryOrDefaultAsync<TResult>(
            objectId,
            queryName,
            args,
            new DurableObjectCallOptions(cancellationToken: cancellationToken));

    /// <inheritdoc/>
    [return: MaybeNull]
    public async Task<TResult> QueryOrDefaultAsync<TResult>(
        string objectId,
        string queryName,
        object?[]? args,
        DurableObjectCallOptions callOptions)
    {
        try
        {
            return await DurableObjectQuery
                .ExecuteAsync<TResult>(
                    _client,
                    objectId,
                    queryName,
                    args ?? [],
                    callOptions)
                .ConfigureAwait(false);
        }
        catch (DurableObjectNotFoundException)
        {
#pragma warning disable CS8603 // Possible null reference return — intentional; [return: MaybeNull] documents this
            return default;
#pragma warning restore CS8603
        }
        catch (DurableObjectNotActiveException)
        {
#pragma warning disable CS8603 // Possible null reference return — intentional; [return: MaybeNull] documents this
            return default;
#pragma warning restore CS8603
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> ListDurableObjectsAsync<T>(
        bool runningOnly = true,
        CancellationToken cancellationToken = default)
        where T : IDurableObject
    {
#if NETCOREAPP3_0_OR_GREATER
        return ListDurableObjectsAsyncCore<T>(runningOnly, cancellationToken);
#else
        // This branch is compiled only into the netstandard2.1 asset. .NET 8+ consumers receive
        // the net8.0 asset, where the SDK's ListWorkflowsAsync API is available.
        throw new PlatformNotSupportedException(
            $"{nameof(ListDurableObjectsAsync)} is not available in the netstandard2.1 binary. " +
            "Use the net8.0 asset on .NET 8 or later to enumerate Durable Objects.");
#endif
    }

#if NETCOREAPP3_0_OR_GREATER
    private async IAsyncEnumerable<string> ListDurableObjectsAsyncCore<T>(
        bool runningOnly,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where T : IDurableObject
    {
        var workflowType = DurableObjectNaming.ResolveWorkflowType(typeof(T));
        var query = runningOnly
            ? $"WorkflowType = '{workflowType}' AND ExecutionStatus = 'Running'"
            : $"WorkflowType = '{workflowType}'";

        // Stream results — no buffering into a list. IAsyncEnumerable is the v1 API contract;
        // callers that need a snapshot use await foreach with ToListAsync().
        await foreach (var exec in _client.ListWorkflowsAsync(query)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return exec.Id;
        }
    }
#endif

    /// <inheritdoc/>
    public Task<ScheduleHandle> CreateDurableObjectScheduleAsync<T>(
        string scheduleId,
        string objectId,
        ScheduleSpec spec,
        string taskQueue,
        ScheduleOverlapPolicy overlap = ScheduleOverlapPolicy.Skip,
        ScheduleOptions? scheduleOptions = null,
        CancellationToken cancellationToken = default)
        where T : IDurableObject =>
        _client.CreateDurableObjectScheduleAsync<T>(
            scheduleId, objectId, spec, taskQueue, overlap, scheduleOptions, cancellationToken);

    /// <inheritdoc/>
    public Task<ScheduleHandle> CreateDurableObjectReminderAsync<T>(
        string scheduleId,
        string targetObjectId,
        string reminderName,
        ScheduleSpec spec,
        string taskQueue,
        ScheduleOverlapPolicy overlap = ScheduleOverlapPolicy.Skip,
        ScheduleOptions? scheduleOptions = null,
        CancellationToken cancellationToken = default)
        where T : IReminderReceiver =>
        _client.CreateDurableObjectReminderAsync<T>(
            scheduleId, targetObjectId, reminderName, spec, taskQueue, overlap, scheduleOptions, cancellationToken);

    private T GetClient<T>(
        string objectId,
        string taskQueue,
        DurableObjectCallOptions? callOptions)
        where T : IDurableObject
    {
        var workflowType = DurableObjectNaming.ResolveWorkflowType(typeof(T));
        var invoker = new DurableObjectClientInvoker(
            _client, objectId, workflowType, taskQueue, callOptions);
        return DurableObjectGeneratedClientRegistry.TryCreate<T>(invoker, out var generated)
            ? generated!
            : DurableObjectProxy<T>.Create(_client, objectId, taskQueue, callOptions);
    }
}
