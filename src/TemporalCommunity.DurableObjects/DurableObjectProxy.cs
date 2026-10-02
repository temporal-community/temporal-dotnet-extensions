using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects.Polyfills;

namespace TemporalCommunity.DurableObjects;

#pragma warning disable CA1812 // Internal class never instantiated — instantiated via DispatchProxy.Create<T, TProxy>() reflection

/// <summary>
/// DispatchProxy-based implementation of <typeparamref name="T"/> that routes every method call
/// to Temporal RPC. This internal type is the base class for the runtime-generated proxy;
/// callers obtain instances only through <see cref="IDurableObjectFactory.Get{T}(string)"/>
/// and its overloads.
/// </summary>
/// <typeparam name="T">A DurableObject interface that extends <see cref="IDurableObject"/>.</typeparam>
internal class DurableObjectProxy<T> : DispatchProxy
{
    // Bug 4 fix A — open generic MethodInfo cached once per proxy type (no nameof + string concat).
    private static readonly MethodInfo s_executeUpdateAsyncGeneric =
        typeof(DurableObjectProxy<T>)
            .GetMethod(nameof(ExecuteUpdateAsyncGeneric), BindingFlags.NonPublic | BindingFlags.Instance)!;

    // Bug 4 fix A — closed generics cached per result type so MakeGenericMethod is not called on
    // every hot dispatch path.
    private static readonly ConcurrentDictionary<Type, MethodInfo> s_closedMethodCache = new();

    private ITemporalClient _client = null!;
    private string _objectId = null!;
    private string _workflowType = null!;
    private string _taskQueue = null!;
    private DurableObjectCallOptions _callOptions = null!;

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Throw.IfNull(targetMethod, nameof(targetMethod));

        var queryAttribute = targetMethod.GetCustomAttribute<WorkflowQueryAttribute>();
        var updateAttribute = targetMethod.GetCustomAttribute<WorkflowUpdateAttribute>();
        var isQuery = queryAttribute is not null;
        var callArgs = args ?? Array.Empty<object?>();

        // The SDK trims a trailing "Async" from update names (WorkflowUpdateDefinition.cs:156-161)
        // but NOT from query names. Mirror that convention here.
        var rpcName = isQuery ? queryAttribute!.Name : updateAttribute?.Name;
        rpcName ??= targetMethod.Name;
        if (!isQuery && updateAttribute?.Name is null &&
            rpcName.EndsWith("Async", StringComparison.Ordinal))
            rpcName = rpcName[..^"Async".Length];

        // Task (void update)
        if (targetMethod.ReturnType == typeof(Task))
            return ExecuteUpdateAsync(rpcName, callArgs);

        // Task<TResult> update
        if (targetMethod.ReturnType.IsGenericType &&
            targetMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var returnType = targetMethod.ReturnType.GetGenericArguments()[0];
            var closed = s_closedMethodCache.GetOrAdd(
                returnType,
                t => s_executeUpdateAsyncGeneric.MakeGenericMethod(t));
            return InvokeUnwrapped(closed, [rpcName, callArgs]);
        }

        // Synchronous query
        if (isQuery)
        {
            var syncMethod = typeof(DurableObjectProxy<T>)
                .GetMethod(nameof(QuerySync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(targetMethod.ReturnType);
            return InvokeUnwrapped(syncMethod, [rpcName, callArgs]);
        }

        throw new InvalidOperationException(
            $"Method '{targetMethod.Name}' on '{typeof(T).Name}' must return Task, Task<T>, " +
            "or be a synchronous [WorkflowQuery].");
    }

    // DispatchProxy.Invoke wraps exceptions from MethodInfo.Invoke in TargetInvocationException.
    // Unwrap so callers see and can catch the real exception (e.g. DurableObjectNotFoundException).
    private object? InvokeUnwrapped(MethodInfo method, object?[] parameters)
    {
        try
        {
            return method.Invoke(this, parameters);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; // unreachable — keeps the compiler happy
        }
    }

    // Start-if-needed plus update submission in one operation; this avoids a read-then-start race,
    // but a failed update may still leave the workflow started.
    // UseExisting means an already-running object is updated rather than starting a duplicate.
    private async Task ExecuteUpdateAsync(string updateName, object?[] args)
    {
        try
        {
            await _client.ExecuteUpdateWithStartWorkflowAsync(
                updateName, args, BuildUpdateOptions())
                .ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            throw new DurableObjectNotFoundException(_objectId, ex);
        }
        catch (WorkflowQueryRejectedException ex)
        {
            throw new DurableObjectNotActiveException(_objectId, ex.WorkflowStatus, ex);
        }
        // WorkflowUpdateFailedException is intentionally NOT mapped — it carries the application's
        // own ApplicationFailureException and is meaningful to callers. All other exceptions rethrow.
    }

    private async Task<TResult> ExecuteUpdateAsyncGeneric<TResult>(string updateName, object?[] args)
    {
        try
        {
            return await _client.ExecuteUpdateWithStartWorkflowAsync<TResult>(
                updateName, args, BuildUpdateOptions())
                .ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            throw new DurableObjectNotFoundException(_objectId, ex);
        }
        catch (WorkflowQueryRejectedException ex)
        {
            throw new DurableObjectNotActiveException(_objectId, ex.WorkflowStatus, ex);
        }
        // WorkflowUpdateFailedException is intentionally NOT mapped.
    }

    // A synchronous interface query method (e.g. `int GetCount()`) must return its value from
    // DispatchProxy.Invoke synchronously, so this blocks on the shared async query core.
    // Callers that must not park a thread use IDurableObjectFactory.QueryDurableObjectAsync instead.
    private TResult QuerySync<TResult>(string queryName, object?[] args) =>
        DurableObjectQuery.ExecuteAsync<TResult>(_client, _objectId, queryName, args, _callOptions)
            .GetAwaiter().GetResult();

    private WorkflowUpdateWithStartOptions BuildUpdateOptions() =>
        new(BuildStartOperation()) { Rpc = _callOptions.ToRpcOptions() };

    private WithStartWorkflowOperation<WorkflowHandle> BuildStartOperation() =>
        WithStartWorkflowOperation.Create(
            _workflowType,
            Array.Empty<object?>(),
            new WorkflowOptions(id: _objectId, taskQueue: _taskQueue)
            {
                IdConflictPolicy = WorkflowIdConflictPolicy.UseExisting,
            });

    /// <summary>
    /// Creates a proxy for <typeparamref name="T"/> that dispatches to the given Temporal client.
    /// </summary>
    /// <param name="client">The Temporal client to use for RPC.</param>
    /// <param name="objectId">The workflow ID of the target DurableObject.</param>
    /// <param name="taskQueue">The task queue the target worker polls.</param>
    /// <param name="callOptions">Optional per-call transport options.</param>
    /// <returns>A typed proxy implementing <typeparamref name="T"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="T"/> violates the DurableObject method attribute contract
    /// (e.g., a signal method is present, or a Task-returning method lacks <c>[WorkflowUpdate]</c>).
    /// </exception>
    internal static T Create(
        ITemporalClient client,
        string objectId,
        string taskQueue,
        DurableObjectCallOptions? callOptions = null)
    {
        // Bug 4 fix B — validate at create time, not at first call.
        ValidateInterface();

        // DispatchProxy.Create<T, TProxy>() returns T — the cast to DurableObjectProxy<T> is safe
        // because TProxy = DurableObjectProxy<T> is the concrete proxy type.
        var proxy = Create<T, DurableObjectProxy<T>>();
        var doProxy = (DurableObjectProxy<T>)(object)proxy!;
        doProxy._client = client;
        doProxy._objectId = objectId;
        doProxy._taskQueue = taskQueue;
        doProxy._workflowType = DurableObjectNaming.ResolveWorkflowType(typeof(T));
        doProxy._callOptions = callOptions ?? new DurableObjectCallOptions();
        return proxy;
    }

    private static void ValidateInterface()
    {
        var interfaceType = typeof(T);

        // Walk the interface's own methods plus all inherited interface methods.
        foreach (var method in interfaceType.GetMethods())
        {
            var hasUpdate = method.GetCustomAttribute<WorkflowUpdateAttribute>() is not null;
            var hasQuery = method.GetCustomAttribute<WorkflowQueryAttribute>() is not null;
            var hasSignal = method.GetCustomAttribute<WorkflowSignalAttribute>() is not null;

            // Signals are unsupported; mutations use acknowledged updates.
            if (hasSignal)
            {
                throw new InvalidOperationException(
                    $"Method '{method.Name}' on '{interfaceType.Name}' carries [WorkflowSignal]. " +
                    "Signals are not supported on DurableObjects in v1. Use [WorkflowUpdate] instead. " +
                    "See docs/DURABLE_OBJECTS.md for supported contract methods.");
            }

            var returnsTask = method.ReturnType == typeof(Task);
            var returnsGenericTask = method.ReturnType.IsGenericType &&
                                     method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>);
            var returnsAnyTask = returnsTask || returnsGenericTask;

            if (returnsAnyTask)
            {
                // Task-returning methods must be [WorkflowUpdate]. The SDK forbids Task-returning
                // [WorkflowQuery] (WorkflowQueryDefinition.AssertValid throws "cannot return a Task").
                if (!hasUpdate)
                {
                    throw new InvalidOperationException(
                        $"Method '{method.Name}' on '{interfaceType.Name}' returns " +
                        $"'{method.ReturnType.Name}' but does not carry [WorkflowUpdate]. " +
                        "Task-returning DurableObject methods must be [WorkflowUpdate].");
                }
            }
            else
            {
                // Non-Task (synchronous) methods must be [WorkflowQuery].
                if (!hasQuery)
                {
                    throw new InvalidOperationException(
                        $"Method '{method.Name}' on '{interfaceType.Name}' returns " +
                        $"'{method.ReturnType.Name}' (non-Task) but does not carry [WorkflowQuery]. " +
                        "Synchronous DurableObject methods must be [WorkflowQuery].");
                }
            }
        }
    }
}
