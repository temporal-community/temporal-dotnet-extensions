using Temporalio.Exceptions;
using Temporalio.Worker.Interceptors;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.IntegrationTests.Infrastructure;
using TemporalCommunity.DurableObjects.IntegrationTests.Objects;
using Xunit;

namespace TemporalCommunity.DurableObjects.IntegrationTests.Scenarios;

/// <summary>
/// Scenario N: ONE worker interceptor applies four cross-cutting concerns.
/// (a) Unauthorized update rejected → caller sees WorkflowUpdateFailedException; object alive.
/// (b) Two overlapping slow updates serialize correctly (8 then 9, not 9,9).
/// (c) After DeactivateAsync, subsequent update rejected with ObjectDeactivating; object terminates.
/// (d) Arbitrary exception from user handler → WorkflowUpdateFailedException(UnhandledUpdateException); object alive.
/// </summary>
public sealed class ScenarioN_WorkerInterceptorInvariants : DurableObjectTestBase
{
    private const string Token = "s3cr3t";

    public ScenarioN_WorkerInterceptorInvariants(WorkflowEnvironmentFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Interceptor_EnforcesAuth_Serialization_DrainWindow_ExceptionWrapping()
    {
        var tq = UniqueTaskQueue();

        // Authorization policy: "Withdraw" requires the correct token as its last arg.
        bool Authorize(HandleUpdateInput input) =>
            input.Update != "Withdraw" ||
            (input.Args.LastOrDefault() as string) == Token;

        var interceptorOptions = new DurableObjectWorkerOptions { Authorize = Authorize };

        using var worker = ScenarioWorkerBuilder.Build(
            Client, tq,
            workflowTypes: [typeof(GuardedCounter)],
            options: interceptorOptions);

        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);
        try
        {
            var factory = TestFactory.Create(Client, tq);
            var obj = factory.Get<IGuardedCounter>("scenario-n-obj");

            // --- (a) Authorization ---
            await obj.IncrementAsync(10); // count = 10

            // Unauthorized withdraw — wrong token.
            var rejected = false;
            try { await obj.WithdrawAsync(3, "wrong-token"); }
            catch (WorkflowUpdateFailedException) { rejected = true; }
            Assert.True(rejected, "Expected unauthorized update to be rejected");

            var afterBad = obj.GetCount();
            Assert.Equal(10, afterBad); // Rejected update mutated nothing.

            // Authorized withdraw.
            await obj.WithdrawAsync(3, Token);
            var afterGood = obj.GetCount();
            Assert.Equal(7, afterGood);

            // --- (b) Serialization ---
            // Two concurrent slow increments from count=7 must serialize: 8 then 9.
            var t1 = factory.Get<IGuardedCounter>("scenario-n-obj").SlowIncrementAsync(1);
            var t2 = factory.Get<IGuardedCounter>("scenario-n-obj").SlowIncrementAsync(1);
            var slow = (await Task.WhenAll(t1, t2)).OrderBy(x => x).ToArray();
            Assert.Equal([8, 9], slow);

            // --- (d) Exception safety net (test before deactivation so the object stays alive) ---
            var unhandledRejected = false;
            WorkflowUpdateFailedException? unhandledEx = null;
            try { await obj.ThrowingUpdateAsync(); }
            catch (WorkflowUpdateFailedException ex) { unhandledRejected = true; unhandledEx = ex; }

            Assert.True(unhandledRejected, "Expected unhandled exception to surface as WorkflowUpdateFailedException");
            // Object still alive — verify by querying.
            var countAfterThrow = obj.GetCount();
            Assert.Equal(9, countAfterThrow);

            // --- (c) Drain-window gate ---
            // Issue DeactivateAsync — the interceptor/run loop will start draining.
            await obj.DeactivateAsync();

            // Immediately after deactivation is accepted, new updates must be rejected.
            // (The object may complete very quickly; the race window is narrow but real.)
            // We verify this by attempting an update and expecting either ObjectDeactivating or
            // the workflow has already closed (DurableObjectNotActiveException or similar).
            // In practice the workflow terminates quickly after drain completes.
            // We accept either outcome as valid — the important thing is no update sneaks through.
            var drainRejected = false;
            try { await factory.Get<IGuardedCounter>("scenario-n-obj").IncrementAsync(1); }
            catch (WorkflowUpdateFailedException ex)
                when (ex.InnerException is ApplicationFailureException afe
                      && afe.ErrorType == "ObjectDeactivating")
            {
                drainRejected = true;
            }
            catch (DurableObjectNotActiveException)
            {
                // Object already closed — acceptable; deactivation completed successfully.
                drainRejected = true;
            }
            catch (Temporalio.Exceptions.RpcException)
            {
                // Workflow may have already completed — acceptable.
                drainRejected = true;
            }

            Assert.True(drainRejected, "Expected post-deactivation update to be rejected");
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
