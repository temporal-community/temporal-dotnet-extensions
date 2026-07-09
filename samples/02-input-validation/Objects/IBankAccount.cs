using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.InputValidation.Objects;

/// <summary>
/// Contract for a bank account DurableObject.
/// Demonstrates deposit, withdraw with balance validation, and explicit deactivation.
/// </summary>
[Workflow]
public interface IBankAccount : IDurableObject
{
    /// <summary>Deposits a positive amount. Throws ArgumentException for non-positive amounts.</summary>
    [WorkflowUpdate]
    Task DepositAsync(decimal amount);

    /// <summary>
    /// Withdraws an amount from the balance.
    /// Validated by [WorkflowUpdateValidator] before the update is applied.
    /// Throws InvalidOperationException("Insufficient funds") if balance would go negative.
    /// </summary>
    [WorkflowUpdate]
    Task WithdrawAsync(decimal amount);

    /// <summary>Returns the current balance (read-only, no await).</summary>
    [WorkflowQuery]
    decimal GetBalance();

    /// <summary>Closes the account. Subsequent operations will throw DurableObjectNotActiveException.</summary>
    [WorkflowUpdate]
    Task CloseAccountAsync();
}
