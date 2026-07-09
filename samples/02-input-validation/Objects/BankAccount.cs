#pragma warning disable CA1822 // Workflow methods must be instance methods
#pragma warning disable CA2007 // ConfigureAwait — workflow code must use ConfigureAwait(true), never false
#pragma warning disable CA1849 // Deactivate() is the correct internal self-deactivation API (not async)
using Microsoft.Extensions.Logging;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.InputValidation.Objects;

/// <summary>
/// A bank account DurableObject that demonstrates input validation patterns:
/// - ArgumentException thrown inside an update for invalid input
/// - [WorkflowUpdateValidator] for pre-update balance checks
/// - Deactivate() for self-termination
/// </summary>
[Workflow]
public sealed class BankAccount : DurableObjectBase, IBankAccount
{
    private readonly string _accountId;
    private decimal _balance;

    /// <summary>
    /// Constructor. Temporal calls this with the workflow start arguments.
    /// </summary>
    [WorkflowInit]
    public BankAccount(string accountId, decimal initialBalance = 0)
    {
        _accountId = accountId;
        _balance = initialBalance;
    }

    /// <summary>
    /// Required boilerplate. Temporal does not inherit [WorkflowRun].
    /// </summary>
    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    /// <inheritdoc/>
    [WorkflowUpdate]
    public Task DepositAsync(decimal amount)
    {
        // Validate input inside the update — the exception is surfaced to the caller.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount, nameof(amount));

        _balance += amount;
        Workflow.Logger.LogInformation(
            "Account {Id}: deposited {Amount:C}, new balance {Balance:C}",
            _accountId, amount, _balance);
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
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Withdrawal amount must be positive.");
        }

        if (_balance < amount)
        {
            throw new InvalidOperationException(
                $"Insufficient funds: balance is {_balance:C}, cannot withdraw {amount:C}.");
        }
    }

    /// <inheritdoc/>
    [WorkflowUpdate]
    public Task WithdrawAsync(decimal amount)
    {
        // The validator (ValidateWithdrawAsync) already ensured balance >= amount.
        _balance -= amount;
        Workflow.Logger.LogInformation(
            "Account {Id}: withdrew {Amount:C}, new balance {Balance:C}",
            _accountId, amount, _balance);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    [WorkflowQuery]
    public decimal GetBalance() => _balance;

    /// <inheritdoc/>
    [WorkflowUpdate]
    public Task CloseAccountAsync()
    {
        Workflow.Logger.LogInformation("Account {Id}: closing.", _accountId);
        // Deactivate() sets the internal flag so the run loop exits after this handler returns.
        Deactivate();
        return Task.CompletedTask;
    }
}
