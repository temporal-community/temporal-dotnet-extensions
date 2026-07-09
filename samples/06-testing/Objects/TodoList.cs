// TodoList.cs — Concrete DurableObject implementation.
//
// Key rules for workflow code (enforced here as a reference pattern):
//
//   1. ConfigureAwait(true) on every await — workflow continuations MUST stay on the
//      Temporal task scheduler. ConfigureAwait(false) routes to the thread pool, causing
//      InvalidWorkflowSchedulerException and replay divergence.
//
//   2. No non-deterministic APIs — DateTime.Now, Guid.NewGuid(), Random, Thread.Sleep,
//      System.IO, and plain Task.Delay are all banned inside workflow code. Use Workflow.UtcNow,
//      Workflow.DelayAsync, and Temporal activities for any non-deterministic operations.
//
//   3. Workflow.Logger not ILogger — Workflow.Logger is replay-aware. ILogger injected via DI
//      would log on every replay, producing misleading duplicate log lines.
//
//   4. [WorkflowRun] must appear on the concrete class — the Temporal SDK does NOT inherit
//      attributes from base classes or interfaces. DurableObjectBase provides DurableObjectRunAsync()
//      as the run loop implementation; the concrete class just delegates to it.

#pragma warning disable CA1822 // Workflow methods must be instance methods — the SDK calls them via reflection on the instance
#pragma warning disable CA2007 // Do not use ConfigureAwait — this pragma is suppressed; we explicitly use ConfigureAwait(true) below

using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.Testing.Objects;

/// <summary>
/// Persistent todo list backed by Temporal workflow history.
/// Demonstrates the minimal DurableObject implementation pattern.
/// </summary>
[Workflow]
public class TodoList : DurableObjectBase, ITodoList
{
    // Instance fields hold in-memory state. They survive within a single workflow execution
    // (even across many updates) but do NOT survive ContinueAsNew. For objects that run
    // indefinitely, override OnBeforeContinueAsNewAsync to serialize state and add a
    // [WorkflowInit] constructor to rehydrate it on the new run. This sample keeps state
    // simple to focus on the testing patterns.
    private readonly List<string> _pending = [];
    private readonly List<string> _completed = [];

    // [WorkflowRun] is REQUIRED on the concrete class (not on the interface or base).
    // The Temporal SDK uses IsDefined(attr, inherit: false) when scanning workflow types,
    // so the attribute must be declared directly here. DurableObjectRunAsync() is the
    // full run-loop implementation provided by DurableObjectBase.
    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    /// <inheritdoc />
    [WorkflowUpdate]
    public Task AddItemAsync(string item)
    {
        // Validate input inside the workflow — ArgumentException propagates back to the
        // caller through the proxy as a WorkflowUpdateFailedException wrapping the original.
        // Tests in TodoListValidationTests.cs demonstrate catching this.
        if (string.IsNullOrWhiteSpace(item))
        {
            throw new ArgumentException("Item cannot be null or empty.", nameof(item));
        }

        _pending.Add(item);

        // No await needed here — all state changes are synchronous list mutations.
        // This is perfectly fine in a DurableObject: the update is durably recorded
        // by the Temporal SDK before this method returns to the caller.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    [WorkflowUpdate]
    public Task CompleteItemAsync(string item)
    {
        // Business rule violation: moving an item that isn't pending.
        // InvalidOperationException propagates to the caller through the proxy.
        if (!_pending.Remove(item))
        {
            throw new InvalidOperationException(
                $"Item '{item}' is not in the pending list and cannot be completed.");
        }

        _completed.Add(item);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    [WorkflowQuery]
    public IReadOnlyList<string> GetPendingItems() => _pending.AsReadOnly();

    /// <inheritdoc />
    [WorkflowQuery]
    public IReadOnlyList<string> GetCompletedItems() => _completed.AsReadOnly();
}
