// TodoListValidationTests.cs — Tests for error cases and boundary conditions.
//
// These tests demonstrate how exceptions thrown inside DurableObject update handlers
// propagate back to the caller through the proxy, and how to use the "try-get" query
// pattern for objects that may or may not exist.
//
// Exception propagation model:
//
//   [WorkflowUpdate] handler throws         =>  WorkflowUpdateFailedException
//     .InnerException                        =>  ApplicationFailureException (UnhandledUpdateException)
//       .InnerException                      =>  ApplicationFailureException (original type indicated by .ErrorType)
//
//   Query against missing object             =>  DurableObjectNotFoundException
//   Query against deactivated object         =>  DurableObjectNotActiveException
//   QueryOrDefaultAsync (missing/inactive)   =>  returns default (null for reference types)
//
// The WorkflowUpdateFailedException wrapping is a Temporal SDK behavior. The SDK serializes
// the exception to the server as an ApplicationFailure and deserializes it at the caller.
// Your application-level exception becomes the inner cause of that chain.

using Temporalio.Exceptions;
using TemporalCommunity.DurableObjects.Testing.Infrastructure;
using TemporalCommunity.DurableObjects.Testing.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.Testing.Tests;

/// <summary>
/// Tests that validate error cases: missing objects, invalid inputs, and business rule violations.
/// </summary>
public sealed class TodoListValidationTests : DurableObjectTestBase
{
    public TodoListValidationTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    // -------------------------------------------------------------------------
    // Test: Query against a missing object throws DurableObjectNotFoundException
    // -------------------------------------------------------------------------
    // Pattern: use a random object ID that was never activated. Any query (synchronous
    // or async) against this ID must throw DurableObjectNotFoundException — never
    // return a default value silently. This distinction matters: default could be
    // confused with an object that was created but has no data yet.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task QueryDurableObjectAsync_OnMissingObject_ShouldThrowNotFoundException()
    {
        var tq = UniqueTaskQueue();
        using var worker = TestHelper.CreateWorker(Client, tq, typeof(TodoList).Assembly);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        var (factory, provider) = TestHelper.CreateFactory(Client, tq);
        try
        {
            // Use an ID that was never activated.
            // QueryDurableObjectAsync goes directly to the Temporal server — no workflow started
            // for this ID means the server returns NotFound, which the library maps to this exception.
            var ex = await Assert.ThrowsAsync<DurableObjectNotFoundException>(
                () => factory.QueryDurableObjectAsync<IReadOnlyList<string>>(
                    "nonexistent-todo-list",
                    "GetPendingItems"));

            Assert.Equal("nonexistent-todo-list", ex.ObjectId);
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
            await ((IAsyncDisposable)provider).DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Test: QueryOrDefaultAsync returns null/default for missing objects
    // -------------------------------------------------------------------------
    // Pattern: the "try-get" variant of a query. Use this when absence of an object
    // is a normal, expected outcome in your application (not an error condition).
    // The factory catches DurableObjectNotFoundException and DurableObjectNotActiveException
    // internally and returns default(TResult) — null for reference types, 0 for int, etc.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task QueryOrDefaultAsync_OnMissingObject_ShouldReturnDefault()
    {
        var tq = UniqueTaskQueue();
        using var worker = TestHelper.CreateWorker(Client, tq, typeof(TodoList).Assembly);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        var (factory, provider) = TestHelper.CreateFactory(Client, tq);
        try
        {
            // QueryOrDefaultAsync swallows DurableObjectNotFoundException and returns default.
            // For IReadOnlyList<string> (a reference type), default is null.
            var result = await factory.QueryOrDefaultAsync<IReadOnlyList<string>>(
                "also-nonexistent",
                "GetPendingItems");

            Assert.Null(result);
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
            await ((IAsyncDisposable)provider).DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Test: AddItemAsync with null/empty throws ArgumentException
    // -------------------------------------------------------------------------
    // Pattern: argument validation inside an update handler propagates to the caller.
    // The SDK serializes the exception as an ApplicationFailure and the caller receives
    // WorkflowUpdateFailedException. The original ArgumentException is the inner cause
    // of the ApplicationFailureException, which is itself the inner cause of the update
    // failed exception.
    //
    // When to use this pattern:
    //   - Testing that your DurableObject rejects bad input before mutating state.
    //   - Ensuring the validation message is meaningful to callers.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddItem_WithNullOrEmpty_ShouldThrowArgumentException()
    {
        var tq = UniqueTaskQueue();
        using var worker = TestHelper.CreateWorker(Client, tq, typeof(TodoList).Assembly);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        var (factory, provider) = TestHelper.CreateFactory(Client, tq);
        try
        {
            var todoList = await factory.GetOrCreateAsync<ITodoList>("validation-list");

            // The Temporal SDK wraps handler exceptions in WorkflowUpdateFailedException.
            // Unwrap the chain to verify the root cause is the ArgumentException from TodoList.
            var ex = await Assert.ThrowsAsync<WorkflowUpdateFailedException>(
                () => todoList.AddItemAsync(string.Empty));

            // WorkflowUpdateFailedException.InnerException is ApplicationFailureException.
            // ApplicationFailureException.InnerException is the original exception serialized by Temporal SDK.
            // Under .NET SDK, nested exception causes are deserialized as nested ApplicationFailureException instances.
            var appFailure = Assert.IsType<ApplicationFailureException>(ex.InnerException);
            var innerFailure = Assert.IsType<ApplicationFailureException>(appFailure.InnerException);
            Assert.Equal("ArgumentException", innerFailure.ErrorType);

            // The object state must be unchanged — the failed update did not mutate anything.
            Assert.Empty(todoList.GetPendingItems());

            await todoList.DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
            await ((IAsyncDisposable)provider).DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Test: CompleteItemAsync on non-pending item throws InvalidOperationException
    // -------------------------------------------------------------------------
    // Pattern: business rule violation surfaces as an exception through the proxy.
    // Same exception chain as above: WorkflowUpdateFailedException wrapping
    // ApplicationFailureException wrapping InvalidOperationException.
    //
    // This pattern verifies that your DurableObject enforces domain invariants and that
    // violations are observable (not silently swallowed) by callers.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CompleteItem_NotInList_ShouldThrowInvalidOperationException()
    {
        var tq = UniqueTaskQueue();
        using var worker = TestHelper.CreateWorker(Client, tq, typeof(TodoList).Assembly);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        var (factory, provider) = TestHelper.CreateFactory(Client, tq);
        try
        {
            var todoList = await factory.GetOrCreateAsync<ITodoList>("rule-violation-list");

            // Add one item so the object exists and has some state.
            await todoList.AddItemAsync("Existing item");

            // Attempt to complete an item that was never added.
            var ex = await Assert.ThrowsAsync<WorkflowUpdateFailedException>(
                () => todoList.CompleteItemAsync("Item that does not exist"));

            var appFailure = Assert.IsType<ApplicationFailureException>(ex.InnerException);
            var innerFailure = Assert.IsType<ApplicationFailureException>(appFailure.InnerException);
            Assert.Equal("InvalidOperationException", innerFailure.ErrorType);

            // State integrity check: the existing pending item is unaffected by the failed update.
            var pending = todoList.GetPendingItems();
            Assert.Single(pending);
            Assert.Contains("Existing item", pending);

            await todoList.DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
            await ((IAsyncDisposable)provider).DisposeAsync();
        }
    }
}
