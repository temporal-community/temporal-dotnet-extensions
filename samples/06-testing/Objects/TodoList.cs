#pragma warning disable CA1822 // Workflow methods must be instance methods
using System.Collections.ObjectModel;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.Testing.Objects;

public sealed record TodoState(Collection<string> Pending, Collection<string> Completed);

/// <summary>A per-user todo list with typed state carried through Continue-as-New.</summary>
[Workflow]
public class TodoList : DurableObjectBase<TodoState>, ITodoList
{
    [WorkflowInit]
    public TodoList(DurableObjectSnapshot<TodoState>? snapshot = null)
        : base(snapshot, new TodoState([], [])) { }

    // The constructor and run signatures match; the optional snapshot permits a cold start.
    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<TodoState>? snapshot = null) => DurableObjectRunAsync();

    [WorkflowUpdate]
    public Task AddItemAsync(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("Item cannot be null or empty.", nameof(item));

        State.Pending.Add(item);
        return Task.CompletedTask;
    }

    [WorkflowUpdate]
    public Task CompleteItemAsync(string item)
    {
        if (!State.Pending.Remove(item))
            throw new InvalidOperationException($"Item '{item}' is not in the pending list and cannot be completed.");

        State.Completed.Add(item);
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public IReadOnlyList<string> GetPendingItems() => new ReadOnlyCollection<string>(State.Pending);

    [WorkflowQuery]
    public IReadOnlyList<string> GetCompletedItems() => new ReadOnlyCollection<string>(State.Completed);
}
