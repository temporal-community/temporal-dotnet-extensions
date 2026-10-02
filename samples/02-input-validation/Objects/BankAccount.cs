#pragma warning disable CA1822 // Workflow methods must be instance methods
#pragma warning disable CA2007 // ConfigureAwait — workflow code must use ConfigureAwait(true), never false
#pragma warning disable CA1849 // Deactivate() is the correct internal self-deactivation API (not async)
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.InputValidation.Objects;

public sealed record BankAccountState(decimal Balance, bool Closed);

/// <summary>
/// A bank account DurableObject that demonstrates input validation patterns:
/// - ArgumentException thrown inside an update for invalid input
/// - [WorkflowUpdateValidator] for pre-update balance checks
/// - Typed state carried through Continue-as-New, including permanent domain closure
/// </summary>
[Workflow]
public sealed class BankAccount : DurableObjectBase<BankAccountState>, IBankAccount
{
    [WorkflowInit]
    public BankAccount(DurableObjectSnapshot<BankAccountState>? snapshot = null)
        : base(snapshot, new BankAccountState(0, false)) { }

    /// <summary>
    /// Required boilerplate. Temporal does not inherit [WorkflowRun].
    /// </summary>
    [WorkflowRun]
    public Task RunAsync(DurableObjectSnapshot<BankAccountState>? snapshot = null) =>
        DurableObjectRunAsync();

    /// <inheritdoc/>
    [WorkflowUpdate]
    public Task DepositAsync(decimal amount)
    {
        EnsureOpen();
        // Validate input inside the update — the exception is surfaced to the caller.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount, nameof(amount));

        State = State with { Balance = State.Balance + amount };
        Workflow.Logger.LogInformation(
            "Account {Id}: deposited {Amount:C}, new balance {Balance:C}",
            WorkflowId, amount, State.Balance);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Validator for WithdrawAsync. Runs before the update is applied.
    /// The [WorkflowUpdateValidator] attribute binds this method to WithdrawAsync.
    /// Validators must be synchronous and must throw to reject the update.
    /// </summary>
    [WorkflowUpdateValidator(nameof(WithdrawAsync))]
    public void ValidateWithdrawAsync(decimal amount)
    {
        EnsureOpen();
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Withdrawal amount must be positive.");
        }

        if (State.Balance < amount)
        {
            throw new InvalidOperationException(
                $"Insufficient funds: balance is {State.Balance:C}, cannot withdraw {amount:C}.");
        }
    }

    /// <inheritdoc/>
    [WorkflowUpdate]
    public Task WithdrawAsync(decimal amount)
    {
        // The validator (ValidateWithdrawAsync) already ensured balance >= amount.
        State = State with { Balance = State.Balance - amount };
        Workflow.Logger.LogInformation(
            "Account {Id}: withdrew {Amount:C}, new balance {Balance:C}",
            WorkflowId, amount, State.Balance);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public decimal GetBalance() => State.Balance;

    /// <inheritdoc/>
    [WorkflowUpdate]
    public Task CloseAccountAsync()
    {
        State = State with { Closed = true };
        Workflow.Logger.LogInformation("Account {Id}: closed.", WorkflowId);
        // Keep the execution open so this domain state remains authoritative if callers
        // subsequently address the same workflow ID.
        return Task.CompletedTask;
    }

    private void EnsureOpen()
    {
        if (State.Closed)
        {
            throw new InvalidOperationException("The account is closed and cannot be changed.");
        }
    }
}
