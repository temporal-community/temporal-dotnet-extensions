# Input validation

A bank account demonstrates three distinct outcomes: accepted updates, rejected updates, and a
query for an entity that does not exist. It uses the compatibility client and named asynchronous
queries; sample 01 shows generated clients.

## Run

Start `temporal server start-dev`, then from the repository root:

```sh
dotnet run --project samples/02-input-validation
```

Each run uses a new account ID. Deposit 500 and withdraw 200 leave a balance of 300. A withdrawal
of 400 is rejected by `[WorkflowUpdateValidator]`. Closing the account records a permanent domain
flag in `BankAccountState`; the subsequent deposit is rejected and the final balance remains 300.
A query for a never-created account produces `DurableObjectNotFoundException`.

## Patterns worth copying

- Validate argument values and domain state before mutation. A validator is synchronous and runs
  before the handler body. With handlers that await, recheck any state-dependent invariant after
  acquiring serialization: validator timing alone is not a transaction boundary.
- Catch `WorkflowUpdateFailedException` for server-reported validator and handler failures.
  Transport failures and timeouts are separate failures; do not label every exception a validation
  rejection. The demo checks that rejected operations leave the balance unchanged.
- Remote causes are deserialized as `ApplicationFailureException` objects; inspect `ErrorType`
  and the cause chain rather than expecting an original CLR `ArgumentException` instance.
- Keep domain closure in carried state. `DeactivateAsync` ends a workflow execution; a later
  update can cold-start a new execution, so deactivation does not enforce permanent account closure.
- Queries do not create an entity. `DurableObjectNotFoundException` means no retained execution was
  found; `DurableObjectNotActiveException` means the query was rejected against a closed execution.

This sample explains validation and lifecycle semantics, not financial transaction guarantees.
