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
            // Strategy: use a slow update to hold the serialization gate, then queue
            // DeactivateAsync and a subsequent Increment behind it.
            //
            // Timeline with serialize=true:
            //   SlowIncrementAsync holds gate (Workflow.DelayAsync 1s)
            //   ↓ DeactivateAsync queues at gate
            //   ↓ test IncrementAsync queues at gate
            //   SlowIncrementAsync finishes → gate released
            //   DeactivateAsync gets gate → _deactivating = true → releases gate
            //   IncrementAsync gets gate → drain-window check → ObjectDeactivating ✓
            //
            // This avoids the race where DeactivateAsync completes the workflow before the
            // test update arrives — if we await DeactivateAsync first, the workflow finishes
            // and the proxy's update-with-start starts a NEW execution (no rejection).
            var slowTask = factory.Get<IGuardedCounter>("scenario-n-obj").SlowIncrementAsync(1);

            // Wait for the slow update to reach Workflow.DelayAsync inside the workflow
            // (i.e., be registered as an in-progress handler and holding the serialization gate).
            await Task.Delay(TimeSpan.FromMilliseconds(400));

            // Queue DeactivateAsync while the slow update holds the gate.
            var deactivateTask = obj.DeactivateAsync();

            // Let DeactivateAsync arrive at the workflow before the test update.
            await Task.Delay(TimeSpan.FromMilliseconds(100));

            // Queue the test Increment — arrives after DeactivateAsync in the gate queue.
            var drainUpdateTask = factory.Get<IGuardedCounter>("scenario-n-obj").IncrementAsync(1);

            // Expect the test Increment to be rejected with ObjectDeactivating.
            var drainRejected = false;
            try { await drainUpdateTask; }
            catch (WorkflowUpdateFailedException ex)
                when (ex.InnerException is ApplicationFailureException afe
                      && afe.ErrorType == "ObjectDeactivating")
            {
                drainRejected = true;
            }

            // Allow slow update and deactivation to complete normally.
            try { await slowTask; } catch { /* may throw if object deactivated underneath */ }
            try { await deactivateTask; } catch { /* may already be processed */ }

            Assert.True(drainRejected, "Expected post-deactivation update to be rejected with ObjectDeactivating");
        }
        finally
        {
            await cts.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
        }
    }
}
