// TodoListIntegrationTests.cs — Integration tests for the TodoList DurableObject.
//
// These tests exercise the full Temporal stack end-to-end using an in-process test server.
// They are the canonical reference for how to test DurableObject state mutations and queries.
//
// Test structure pattern used throughout:
//
//   var tq = UniqueTaskQueue();                               // 1. Get an isolated task queue
//   using var worker = TestHelper.CreateWorker(...);          // 2. Start a worker on that queue
//   var cts = new CancellationTokenSource();
//   var run = worker.ExecuteAsync(cts.Token);                 // 3. Run the worker in background
//   var (factory, provider) = TestHelper.CreateFactory(...);  // 4. Create the factory (client-side)
//   try
//   {
//       var obj = await factory.GetOrCreateAsync<T>(id);      // 5. Get or create the DurableObject
//       // ... exercise the object ...
//       await obj.DeactivateAsync();                          // 6. Clean up: deactivate the object
//   }
//   finally
//   {
//       await cts.CancelAsync();                              // 7. Stop the worker
//       try { await run; } catch (OperationCanceledException) { }
//       await ((IAsyncDisposable)provider).DisposeAsync();    // 8. Dispose the service provider
//   }
//
// The try/finally ensures the worker is stopped and the provider is disposed even when a
// test assertion fails.

using TemporalCommunity.DurableObjects.Testing.Infrastructure;
using TemporalCommunity.DurableObjects.Testing.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.Testing.Tests;

/// <summary>
/// Integration tests that exercise TodoList state transitions against a real Temporal server.
/// </summary>
public sealed class TodoListIntegrationTests : DurableObjectTestBase
{
    public TodoListIntegrationTests(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    // -------------------------------------------------------------------------
    // Test: AddItemAsync — item appears in pending list
    // -------------------------------------------------------------------------
    // Pattern: create the object, call one update, query the result.
    // This is the baseline "does anything work at all" test for your DurableObject.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddItem_ShouldAppearInPendingList()
    {
        // Pattern: each test gets its own task queue so no two tests share workflow history.
        var tq = UniqueTaskQueue();
        using var worker = TestHelper.CreateWorker(Client, tq, typeof(TodoList).Assembly);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        // Use a unique object ID per test — workflow IDs are namespace-scoped, so shared IDs
        // across tests are only safe due to xUnit's serial-within-class default. Unique IDs
        // make tests safe under any parallelism setting.
        var objectId = $"todos-{Guid.NewGuid():N}";
        var (factory, provider) = TestHelper.CreateFactory(Client, tq);
        try
        {
            // Pattern: GetOrCreateAsync is preferred over Get in tests.
            // GetOrCreateAsync issues a start-with-update RPC that atomically activates the
            // workflow and sends the first update in one round trip. Get() returns a proxy
            // immediately without verifying the workflow exists — if no update has been sent yet,
            // there is nothing for the worker to execute.
            var todoList = await factory.GetOrCreateAsync<ITodoList>(objectId);

            await todoList.AddItemAsync("Buy groceries");

            var pending = todoList.GetPendingItems();

            Assert.Contains("Buy groceries", pending);
            Assert.Empty(todoList.GetCompletedItems());

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
    // Test: CompleteItemAsync — item moves from pending to completed
    // -------------------------------------------------------------------------
    // Pattern: multi-step state transition. Add an item, then complete it.
    // Verifies that the pending list shrinks and the completed list grows.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CompleteItem_ShouldMoveFromPendingToCompleted()
    {
        var tq = UniqueTaskQueue();
        using var worker = TestHelper.CreateWorker(Client, tq, typeof(TodoList).Assembly);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        var objectId = $"todos-{Guid.NewGuid():N}";
        var (factory, provider) = TestHelper.CreateFactory(Client, tq);
        try
        {
            var todoList = await factory.GetOrCreateAsync<ITodoList>(objectId);

            await todoList.AddItemAsync("Write tests");
            await todoList.CompleteItemAsync("Write tests");

            // After completion the item must be absent from pending and present in completed.
            Assert.DoesNotContain("Write tests", todoList.GetPendingItems());
            Assert.Contains("Write tests", todoList.GetCompletedItems());

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
    // Test: Multiple updates — state accumulates across calls
    // -------------------------------------------------------------------------
    // Pattern: call several updates, then query — verifies that Temporal persists
    // each state change in the workflow history and the object sees all of them.
    // This is the critical proof that the DurableObject is actually durable.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MultipleUpdates_ShouldAccumulateState()
    {
        var tq = UniqueTaskQueue();
        using var worker = TestHelper.CreateWorker(Client, tq, typeof(TodoList).Assembly);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        var objectId = $"todos-{Guid.NewGuid():N}";
        var (factory, provider) = TestHelper.CreateFactory(Client, tq);
        try
        {
            var todoList = await factory.GetOrCreateAsync<ITodoList>(objectId);

            // Add three items.
            await todoList.AddItemAsync("Task A");
            await todoList.AddItemAsync("Task B");
            await todoList.AddItemAsync("Task C");

            // Complete one.
            await todoList.CompleteItemAsync("Task B");

            // Query: two pending, one completed.
            var pending = todoList.GetPendingItems();
            var completed = todoList.GetCompletedItems();

            Assert.Equal(2, pending.Count);
            Assert.Contains("Task A", pending);
            Assert.Contains("Task C", pending);
            Assert.Single(completed);
            Assert.Contains("Task B", completed);

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
    // Test: GetOrCreateAsync is idempotent
    // -------------------------------------------------------------------------
    // Pattern: call GetOrCreateAsync twice with the same object ID and verify the
    // second call returns a proxy to the same running workflow — not a duplicate start.
    // This is the "exactly-once activation" guarantee of DurableObjects.
    //
    // How it works: GetOrCreateAsync uses StartWorkflow with UseExisting conflict policy,
    // which Temporal implements as an atomic "start if not running, attach if running"
    // operation. The second call sees the workflow already running and returns a proxy
    // to the same execution without starting a new one.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOrCreateAsync_ShouldStartObjectExactlyOnce()
    {
        var tq = UniqueTaskQueue();
        using var worker = TestHelper.CreateWorker(Client, tq, typeof(TodoList).Assembly);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        var sharedListId = $"todos-{Guid.NewGuid():N}";
        var (factory, provider) = TestHelper.CreateFactory(Client, tq);
        try
        {
            // First call: activates the workflow and adds an item.
            var firstProxy = await factory.GetOrCreateAsync<ITodoList>(sharedListId);
            await firstProxy.AddItemAsync("Item from first call");

            // Second call with the same ID: must attach to the existing workflow, not start a new one.
            var secondProxy = await factory.GetOrCreateAsync<ITodoList>(sharedListId);
            await secondProxy.AddItemAsync("Item from second call");

            // Both items are visible through either proxy — they point to the same workflow.
            var pending = firstProxy.GetPendingItems();
            Assert.Equal(2, pending.Count);
            Assert.Contains("Item from first call", pending);
            Assert.Contains("Item from second call", pending);

            await firstProxy.DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
            await ((IAsyncDisposable)provider).DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Test: QueryDurableObjectAsync — async query path
    // -------------------------------------------------------------------------
    // Pattern: use factory.QueryDurableObjectAsync instead of the synchronous proxy query.
    // The proxy's [WorkflowQuery] methods are synchronous because the Temporal SDK's query
    // path blocks internally. QueryDurableObjectAsync is the async escape hatch — it is
    // preferable on hot paths or wherever thread-parking is unacceptable.
    //
    // Note: query names match the method name declared on the interface/concrete class.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task QueryDurableObjectAsync_ShouldReturnCurrentState()
    {
        var tq = UniqueTaskQueue();
        using var worker = TestHelper.CreateWorker(Client, tq, typeof(TodoList).Assembly);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        var objectId = $"todos-{Guid.NewGuid():N}";
        var (factory, provider) = TestHelper.CreateFactory(Client, tq);
        try
        {
            var todoList = await factory.GetOrCreateAsync<ITodoList>(objectId);

            await todoList.AddItemAsync("Async query test item");

            // QueryDurableObjectAsync<TResult>(objectId, queryName) — the query name must match
            // the method name as registered by the SDK (the [WorkflowQuery] method name).
            var pending = await factory.QueryDurableObjectAsync<IReadOnlyList<string>>(
                objectId,
                "GetPendingItems");

            Assert.Single(pending);
            Assert.Equal("Async query test item", pending[0]);

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
