# Sample 06: Testing DurableObjects

A reference test suite demonstrating how to write integration tests for DurableObjects built
with `TemporalCommunity.DurableObjects`. Copy patterns from this project to test your own
DurableObjects.

## What this project shows

- How to start an in-process Temporal server for tests
- How to isolate tests with unique task queues
- How to create and exercise a DurableObject end-to-end
- How to use `GetOrCreateAsync` to guarantee the object exists before asserting
- How to use the async query path (`QueryDurableObjectAsync`)
- How to use the try-get query path (`QueryOrDefaultAsync`)
- How exceptions from update handlers propagate to test assertions

## Running the tests

No Temporal server or Docker required. The in-process test server handles everything.

```bash
dotnet test samples/06-testing
```

On the first run the Temporal test binary is downloaded and cached in `~/.temporalio`.
Subsequent runs use the cached binary and start in seconds.

## Why `WorkflowEnvironment.StartLocalAsync()`?

`WorkflowEnvironment.StartLocalAsync()` boots a full Temporal state machine in-process —
same server behavior as production Temporal, but no external process required. This gives
tests genuine durable-execution semantics (replay, persistence within the test session,
update-with-start atomicity) without the overhead of Docker or a running Temporal CLI server.

The fixture is shared across all tests via `ICollectionFixture<WorkflowEnvironmentFixture>`,
so the server starts once per test run rather than once per test class.

## Why each test needs a unique task queue

Two tests running concurrently on the same task queue would have their workers compete for
tasks. One worker could pick up the other test's workflow and cause replay errors or
incorrect state — classic cross-test contamination. `UniqueTaskQueue()` generates a GUID-based
name so each test's worker and workflow history are fully isolated.

## Why `GetOrCreateAsync` is preferred over `Get` in tests

`Get<T>(objectId)` returns a proxy immediately but issues no RPC. If the object has never
been activated, the proxy will work for updates (which use update-with-start) but will throw
`DurableObjectNotFoundException` on any query before the first update completes.

`GetOrCreateAsync<T>(objectId)` issues a `StartWorkflow` RPC with `UseExisting` conflict
policy — a race-free "start if not running" guarantee. After this call returns, the workflow
is guaranteed to exist and queries can be issued safely.

In tests, prefer `GetOrCreateAsync` so your test setup is unambiguous.

## Exception propagation from update handlers

When an update handler throws, the Temporal SDK wraps the exception before delivering it to
the caller:

```
WorkflowUpdateFailedException
  .InnerException: ApplicationFailureException   (Temporal's serialized failure)
    .InnerException: ArgumentException / InvalidOperationException / ...   (your original exception)
```

Tests that verify handler validation should catch `WorkflowUpdateFailedException` and assert
on the inner chain:

```csharp
var ex = await Assert.ThrowsAsync<WorkflowUpdateFailedException>(
    () => todoList.AddItemAsync(string.Empty));

var appFailure = Assert.IsType<ApplicationFailureException>(ex.InnerException);
Assert.IsType<ArgumentException>(appFailure.InnerException);
```

## How to extend this pattern for your own DurableObjects

1. Create your interface (`IMyObject : IDurableObject`) with `[WorkflowUpdate]` and `[WorkflowQuery]` methods.
2. Create your concrete class extending `DurableObjectBase` and implementing your interface.
3. Add `[WorkflowRun] public Task RunAsync() => DurableObjectRunAsync();` to the concrete class.
4. Copy `Infrastructure/` into your test project (or reference it from a shared test utilities project).
5. Derive your test class from `DurableObjectTestBase`.
6. In each test: `UniqueTaskQueue()` → `TestHelper.CreateWorker(...)` → `TestHelper.CreateFactory(...)` → exercise and assert.

## Project structure

```
samples/06-testing/
  Objects/
    ITodoList.cs              DurableObject interface (contract)
    TodoList.cs               DurableObject implementation (run loop + handlers)
  Infrastructure/
    WorkflowEnvironmentFixture.cs   Shared test server (ICollectionFixture)
    DurableObjectTestBase.cs        Base class: client access + UniqueTaskQueue()
    TestHelper.cs                   CreateWorker() and CreateFactory() helpers
  Tests/
    TodoListIntegrationTests.cs     Happy-path state mutation and query tests
    TodoListValidationTests.cs      Error cases: missing objects, invalid input, rule violations
```
