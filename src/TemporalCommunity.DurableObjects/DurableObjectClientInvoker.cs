using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Reflection-free dispatch core used by source-generated DurableObject clients.
/// </summary>
public sealed class DurableObjectClientInvoker
{
    private readonly ITemporalClient _client;
    private readonly string _objectId;
    private readonly string _workflowType;
    private readonly string _taskQueue;
    private readonly DurableObjectCallOptions _defaultCallOptions;

    internal DurableObjectClientInvoker(
        ITemporalClient client,
        string objectId,
        string workflowType,
        string taskQueue,
        DurableObjectCallOptions? defaultCallOptions)
    {
        _client = client;
        _objectId = objectId;
        _workflowType = workflowType;
        _taskQueue = taskQueue;
        _defaultCallOptions = defaultCallOptions ?? new DurableObjectCallOptions();
    }

    /// <summary>
    /// Records a signal, starting the object if needed. Completion acknowledges server receipt,
    /// not activation, authorization, or handler completion.
    /// </summary>
    public async Task SignalAsync(
        string signalName,
        IReadOnlyCollection<object?> args,
        DurableObjectCallOptions? callOptions = null)
    {
        await _client.StartWorkflowAsync(
            _workflowType,
            Array.Empty<object?>(),
            new WorkflowOptions(_objectId, _taskQueue)
            {
                StartSignal = signalName,
                StartSignalArgs = args,
                Rpc = (callOptions ?? _defaultCallOptions).ToRpcOptions(),
            }).ConfigureAwait(false);
    }

    /// <summary>Starts the object when needed and submits the update without a read-then-start race.</summary>
    public async Task ExecuteUpdateAsync(
        string updateName,
        IReadOnlyCollection<object?> args,
        DurableObjectCallOptions? callOptions = null)
    {
        try
        {
            await _client.ExecuteUpdateWithStartWorkflowAsync(
                updateName,
                args,
                BuildUpdateOptions(callOptions)).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            throw new DurableObjectNotFoundException(_objectId, ex);
        }
    }

    /// <summary>Starts the object when needed and submits the update without a read-then-start race.</summary>
    public async Task<TResult> ExecuteUpdateAsync<TResult>(
        string updateName,
        IReadOnlyCollection<object?> args,
        DurableObjectCallOptions? callOptions = null)
    {
        try
        {
            return await _client.ExecuteUpdateWithStartWorkflowAsync<TResult>(
                updateName,
                args,
                BuildUpdateOptions(callOptions)).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            throw new DurableObjectNotFoundException(_objectId, ex);
        }
    }

    /// <summary>Executes an asynchronous, side-effect-free query against the active object.</summary>
    public Task<TResult> QueryAsync<TResult>(
        string queryName,
        IReadOnlyCollection<object?> args,
        DurableObjectCallOptions? callOptions = null) =>
        DurableObjectQuery.ExecuteAsync<TResult>(
            _client,
            _objectId,
            queryName,
            args.ToArray(),
            callOptions ?? _defaultCallOptions);

    private WorkflowUpdateWithStartOptions BuildUpdateOptions(DurableObjectCallOptions? callOptions) =>
        new(BuildStartOperation())
        {
            Rpc = (callOptions ?? _defaultCallOptions).ToRpcOptions(),
        };

    private WithStartWorkflowOperation<WorkflowHandle> BuildStartOperation() =>
        WithStartWorkflowOperation.Create(
            _workflowType,
            Array.Empty<object?>(),
            new WorkflowOptions(id: _objectId, taskQueue: _taskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
            });
}
