#pragma warning disable CA1812 // Internal class never instantiated — instantiated by Temporal SDK via reflection at runtime
using Temporalio.Common;
using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Internal workflow spawned by a Temporal Schedule on each reminder tick. Its sole
/// responsibility is to execute the <c>ReminderDeliveryActivities.DeliverReminderAsync</c>
/// activity, which issues the <c>OnReminder</c> update to the target canonical DurableObject.
/// </summary>
/// <remarks>
/// <para>
/// This is a framework-internal type — it is registered automatically by
/// <see cref="DurableObjectWorkerExtensions.AddDurableObjectWorkflows(Temporalio.Extensions.Hosting.ITemporalWorkerServiceOptionsBuilder, System.Reflection.Assembly, DurableObjectWorkerOptions?)"/>
/// and is not intended for direct use by application code.
/// </para>
/// <para>
/// <b>Activity name convention:</b> The SDK strips the trailing <c>"Async"</c> suffix from
/// activity method names when deriving the wire name
/// (<c>ActivityDefinition.cs</c> — same convention as workflows). Therefore
/// <c>ReminderDeliveryActivities.DeliverReminderAsync</c> registers as activity name
/// <c>"DeliverReminder"</c>. The string literal is used here to avoid a compile-time dependency
/// on the Phase 4 <c>ReminderDeliveryActivities</c> type while still producing the correct wire
/// name at runtime.
/// </para>
/// </remarks>
[Workflow]
internal sealed class ReminderDispatcher
{
    /// <summary>
    /// Runs the reminder dispatcher workflow: executes the <c>DeliverReminder</c> activity
    /// with the given dispatch payload and returns when delivery completes (or exhausts retries).
    /// </summary>
    /// <param name="dispatch">
    /// The reminder dispatch payload specifying the target object and reminder name.
    /// </param>
#pragma warning disable CA1822 // [WorkflowRun] methods must be instance methods — the SDK requires a non-static entry point
    [WorkflowRun]
    public async Task RunAsync(ReminderDispatch dispatch)
#pragma warning restore CA1822
    {
        // Activity name "DeliverReminder" is derived from ReminderDeliveryActivities.DeliverReminderAsync
        // by SDK Async-stripping convention. String used here to avoid a forward dependency on the
        // Phase 4 ReminderDeliveryActivities type.
        await Workflow.ExecuteActivityAsync(
            "DeliverReminder",
            [dispatch],
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(5),
                RetryPolicy = new RetryPolicy { MaximumAttempts = 10 },
            }).ConfigureAwait(true);
    }
}
