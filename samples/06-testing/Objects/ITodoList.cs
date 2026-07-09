// ITodoList.cs — The DurableObject contract for the TodoList sample.
//
// Design notes:
//   - All mutating operations are [WorkflowUpdate] — callers get acknowledgement when
//     the state change is durably recorded. [WorkflowSignal] is banned by this library.
//   - Read operations are [WorkflowQuery] — synchronous, zero-latency, no round-trip to server.
//   - The interface extends IDurableObject so the factory can type-check and route RPCs.
//   - [Workflow] on the interface is required by the Temporal .NET SDK; it is how the SDK
//     discovers the workflow type name used for routing.

using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.Testing.Objects;

/// <summary>
/// DurableObject contract for a persistent, per-user todo list.
/// Items move through two states: pending (added but not done) and completed.
/// </summary>
[Workflow]
public interface ITodoList : IDurableObject
{
    /// <summary>Adds a new item to the pending list.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="item"/> is null or empty.</exception>
    [WorkflowUpdate]
    Task AddItemAsync(string item);

    /// <summary>Marks a pending item as completed, moving it to the completed list.</summary>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="item"/> is not in the pending list.</exception>
    [WorkflowUpdate]
    Task CompleteItemAsync(string item);

    /// <summary>Returns all items that have not yet been completed.</summary>
    [WorkflowQuery]
    IReadOnlyList<string> GetPendingItems();

    /// <summary>Returns all items that have been marked as completed.</summary>
    [WorkflowQuery]
    IReadOnlyList<string> GetCompletedItems();
}
