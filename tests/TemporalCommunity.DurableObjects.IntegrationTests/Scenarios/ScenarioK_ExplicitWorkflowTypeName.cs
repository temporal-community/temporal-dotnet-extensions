using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Scenarios;

/// <summary>
/// Scenario K: The workflow type name is resolved from ITallyBox's explicit [Workflow("TallyMachine")]
/// rather than the blind I-strip convention (which would derive "TallyBox" and never find the
/// registered workflow). Success = the update and query both reach TallyMachine.
/// </summary>
public sealed class ScenarioK_ExplicitWorkflowTypeName : DurableObjectTestBase
{
    public ScenarioK_ExplicitWorkflowTypeName(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ExplicitWorkflowName_ResolvesCorrectly()
    {
        const string id = "scenario-k-obj";
        var tq = UniqueTaskQueue();

        using var worker = ScenarioWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(TallyMachine)]);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            var factory = TestFactory.Create(Client, tq);
            var tally = factory.Get<ITallyBox>(id);

            // Update: ITallyBox resolves to workflow type "TallyMachine" via [Workflow("TallyMachine")].
            var total = await tally.AddAsync(4);
            // Query: same resolution path.
            var read = tally.Total();

            Assert.Equal(4, total);
            Assert.Equal(4, read);

            await tally.DeactivateAsync();
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
