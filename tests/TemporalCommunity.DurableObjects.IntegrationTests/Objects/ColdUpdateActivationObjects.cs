#pragma warning disable CA1822 // Workflow methods must be instance methods.
#pragma warning disable CA2007 // Workflow code must stay on the workflow scheduler.
using Temporalio.Common;
using Temporalio.Activities;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Objects;

[Workflow]
public interface IColdUpdateActivationObject : IDurableObject
{
    /// <summary>Returns "update:{phase}" as observed when the handler body ran.</summary>
    [WorkflowUpdate]
    Task<string> ObserveUpdateAsync();

    [WorkflowQuery]
    string ReadPhase();
}

/// <summary>
/// OnActivateAsync blocks on a <see cref="Slice3Barrier"/> activity. The update handler records
/// the activation phase it observed. Used to probe whether the FIRST update delivered via
/// update-with-start to a cold object waits for activation (design §3 suspected bug).
/// </summary>
[Workflow]
public sealed class ColdUpdateActivationObject : DurableObjectBase, IColdUpdateActivationObject
{
    private string _phase = "constructed";
    private bool _rollover;

    [WorkflowRun]
    public Task RunAsync() => DurableObjectRunAsync();

    protected override async Task OnActivateAsync()
    {
        _phase = "activating";
        await Workflow.ExecuteActivityAsync(
            (Slice3BarrierActivities activities) => activities.WaitAsync(),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromSeconds(30),
                RetryPolicy = new RetryPolicy { MaximumAttempts = 1 },
            });
        _phase = "active";
    }

    [WorkflowUpdate]
    public async Task<string> ObserveUpdateAsync() => await Workflow.ExecuteActivityAsync(
        (ColdUpdateProbeActivities activities) => activities.ObserveAsync($"update:{_phase}"),
        new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromSeconds(30),
            RetryPolicy = new RetryPolicy { MaximumAttempts = 1 },
        });

    [WorkflowUpdate]
    public Task RequestRolloverAsync()
    {
        _rollover = true;
        return Task.CompletedTask;
    }

    protected override bool ShouldContinueAsNew() => _rollover;

    [WorkflowQuery]
    public string ReadPhase() => _phase;
}

public sealed class ColdUpdateProbeActivities
{
    [Activity("ColdUpdateProbe")]
    public Task<string> ObserveAsync(string phase) => Task.FromResult(phase);
}
