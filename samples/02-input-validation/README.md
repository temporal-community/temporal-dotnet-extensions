# Sample 02 — Input Validation

This sample demonstrates how to validate inputs and handle error conditions with DurableObjects,
using a `BankAccount` object with deposit and withdraw operations.

## What you'll learn

- Throwing `ArgumentException` inside a `[WorkflowUpdate]` to reject bad input
- Using `[WorkflowUpdateValidator]` for pre-update guards (e.g., insufficient funds)
- Catching `DurableObjectNotFoundException` when an object was never created
- Catching `DurableObjectNotActiveException` when an object has been closed
- Calling `Deactivate()` inside an update to self-terminate the workflow execution

## Prerequisites

- [Temporal server running locally](https://docs.temporal.io/cli#start-dev-server): `temporal server start-dev`
- .NET 10 SDK

## Running

```bash
cd samples/02-input-validation
dotnet run
```

## Key patterns

### Validation inside an update

For simple per-argument rules (non-positive amounts, null checks), throw directly inside the
update handler. The exception is surfaced to the caller as a `WorkflowUpdateFailedException`.

```csharp
[WorkflowUpdate]
public Task DepositAsync(decimal amount)
{
    if (amount <= 0)
        throw new ArgumentException("Deposit amount must be positive.", nameof(amount));
    _balance += amount;
    return Task.CompletedTask;
}
```

### Pre-update validators with [WorkflowUpdateValidator]

For guards that depend on current state (e.g., balance checks), use a validator method.
The validator runs *before* the update is applied — if it throws, the update is rejected
without any state change. Validators must be **synchronous** (no awaits) and must be
declared on the concrete class (not the interface).

```csharp
[WorkflowUpdateValidator(nameof(WithdrawAsync))]
public void ValidateWithdrawAsync(decimal amount)
{
    if (_balance < amount)
        throw new InvalidOperationException($"Insufficient funds: balance is {_balance:C}.");
}
```

### Self-termination with Deactivate()

Call `Deactivate()` from inside an update handler to signal the run loop to exit after
the handler returns. Once closed, the object is no longer active — queries throw
`DurableObjectNotActiveException`.

```csharp
[WorkflowUpdate]
public Task CloseAccountAsync()
{
    Deactivate();
    return Task.CompletedTask;
}
```

### Catching update validation failures

When an update validator or update handler throws, the exception is wrapped before it reaches
the caller. The correct catch pattern is:

```csharp
try
{
    await proxy.WithdrawAsync(amount);
}
catch (WorkflowUpdateFailedException ex)
    when (ex.Cause is ApplicationFailureException { Message: var msg })
{
    Console.WriteLine($"Validation rejected: {msg}");
}
```

> **Important:** Update validators and update handlers that throw always deliver a
> `WorkflowUpdateFailedException` to the caller. The original exception is accessible via
> `ex.Cause`. Do not catch `InvalidOperationException` or `ArgumentException` directly —
> they will never be caught.

The full exception chain is:
`WorkflowUpdateFailedException` → `ApplicationFailureException` (via `ex.Cause`) → your original
exception (e.g., `InvalidOperationException`, `ArgumentException`).

### Handling missing / inactive objects

```csharp
try
{
    var balance = await factory.QueryDurableObjectAsync<decimal>("acct-001", "GetBalance");
}
catch (DurableObjectNotFoundException)
{
    // Object was never created, or its history was purged.
}
catch (DurableObjectNotActiveException)
{
    // Object existed but has been deactivated.
}
```
