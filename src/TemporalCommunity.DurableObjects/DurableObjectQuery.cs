using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Shared async core for all DurableObject query dispatches.
/// </summary>
/// <remarks>
/// Queries are read-only and side-effect-free: they never start a workflow execution.
/// <see cref="QueryRejectCondition.NotOpen"/> is set so that a closed or deactivated object
/// surfaces a <see cref="DurableObjectNotActiveException"/> rather than silently returning
/// stale state — making a deactivated object distinguishable from an active one holding a
/// default value.
/// </remarks>
internal static class DurableObjectQuery
{
    /// <summary>
    /// Dispatches a named query to the workflow execution identified by <paramref name="objectId"/>
    /// and returns the deserialized result.
    /// </summary>
    /// <typeparam name="TResult">The expected query result type.</typeparam>
    /// <param name="client">The Temporal client to use for the query RPC.</param>
    /// <param name="objectId">The workflow ID of the target DurableObject.</param>
    /// <param name="queryName">The registered query wire name.</param>
    /// <param name="args">Query arguments; pass an empty array for zero-argument queries.</param>
    /// <param name="callOptions">Optional per-call transport options.</param>
    /// <returns>The deserialized query result.</returns>
    /// <exception cref="DurableObjectNotFoundException">
    /// Thrown when no workflow execution exists for <paramref name="objectId"/>.
    /// </exception>
    /// <exception cref="DurableObjectNotActiveException">
    /// Thrown when the workflow execution is closed or deactivated.
    /// </exception>
    internal static async Task<TResult> ExecuteAsync<TResult>(
        ITemporalClient client,
        string objectId,
        string queryName,
        object?[] args,
        DurableObjectCallOptions? callOptions = null)
    {
        try
        {
            return await client.GetWorkflowHandle(objectId)
                .QueryAsync<TResult>(
                    queryName,
                    args,
                    new WorkflowQueryOptions
                    {
                        RejectCondition = QueryRejectCondition.NotOpen,
                        Rpc = (callOptions ?? new DurableObjectCallOptions()).ToRpcOptions(),
                    })
                .ConfigureAwait(false);
        }
        catch (WorkflowQueryRejectedException ex)
        {
            throw new DurableObjectNotActiveException(objectId, ex.WorkflowStatus, ex);
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            throw new DurableObjectNotFoundException(objectId, ex);
        }
    }
}
