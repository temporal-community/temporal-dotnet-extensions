using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using Temporalio.Client.Schedules;
using Temporalio.Extensions.Hosting;
using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.Scheduling.Activities;
using TemporalCommunity.DurableObjects.Scheduling.Objects;

const string TaskQueue = "scheduling-demo";

var builder = Host.CreateApplicationBuilder(args);

// Register the Temporal client.
builder.Services.AddTemporalClient(opts =>
    opts.TargetHost = builder.Configuration["Temporal:Address"] ?? "localhost:7233");

// Register IDurableObjectFactory so it is available for injection and demo code.
builder.Services.AddDurableObjects(TaskQueue);

// Register ReminderDeliveryActivities in DI so the hosted worker can resolve it.
// AddDurableObjectReminderDelivery<T>() resolves T from DI — a simple AddSingleton<T>()
// registration (no factory lambda) is the correct pattern here.
builder.Services.AddSingleton<ReminderDeliveryActivities>();

// Register the worker.
builder.Services.AddHostedTemporalWorker(TaskQueue)
    .AddDurableObjectWorkflows(Assembly.GetExecutingAssembly())
    // AddDurableObjectReminderDelivery registers ReminderDeliveryActivities from DI,
    // which is required for canonical-object reminder dispatch to work.
    .AddDurableObjectReminderDelivery<ReminderDeliveryActivities>()
    // AddSingletonActivities registers SchedulingActivities in DI and wires its [Activity]
    // methods to the worker. DI injects ILogger<SchedulingActivities> automatically.
    .AddSingletonActivities<SchedulingActivities>();

// Register the demo runner as a hosted service.
builder.Services.AddHostedService<SchedulingDemo>();

await builder.Build().RunAsync();

/// <summary>
/// Background service that drives the scheduling demo.
/// Demonstrates two distinct patterns:
///   1. Per-tick Schedule  — ReportGenerator (fresh execution per tick)
///   2. Canonical Reminder — SubscriptionTracker (one execution, state accumulates)
/// </summary>
internal sealed class SchedulingDemo : BackgroundService
{
    private const string TaskQueue = "scheduling-demo";

    private readonly IDurableObjectFactory _factory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<SchedulingDemo> _logger;

    public SchedulingDemo(
        IDurableObjectFactory factory,
        IHostApplicationLifetime lifetime,
        ILogger<SchedulingDemo> logger)
    {
        _factory = factory;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Brief startup delay so the worker is ready before we create schedules.
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);

            _logger.LogInformation("=== Scheduling Demo: Two Patterns ===");
            _logger.LogInformation("");

            await RunReportGeneratorDemoAsync(stoppingToken).ConfigureAwait(false);
            await RunSubscriptionTrackerDemoAsync(stoppingToken).ConfigureAwait(false);

            _logger.LogInformation("");
            _logger.LogInformation("=== Demo complete. Check Temporal Web UI at http://localhost:8233 ===");
            _logger.LogInformation("  Pattern 1 (Schedule): look for 'report-gen-*' executions — each tick gets a distinct ID");
            _logger.LogInformation("  Pattern 2 (Reminder): look for 'subscription-tracker-demo' — one execution, count grows");

        }
        catch (Exception error) when (!stoppingToken.IsCancellationRequested)
        {
            Environment.ExitCode = 1;
            _logger.LogError(error, "Demo failed");
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }

    private async Task RunReportGeneratorDemoAsync(CancellationToken ct)
    {
        _logger.LogInformation("--- Pattern 1: Per-tick Schedule (ReportGenerator) ---");
        _logger.LogInformation("Each tick spawns a FRESH execution. State does not accumulate.");

        var scheduleId = $"report-schedule-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        var schedHandle = await _factory.CreateDurableObjectScheduleAsync<IReportGenerator>(
            scheduleId,
            objectId: "report-gen",
            new ScheduleSpec
            {
                // Short interval for demo purposes — 10 seconds between ticks.
                Intervals = [new ScheduleIntervalSpec(TimeSpan.FromSeconds(10))],
            },
            taskQueue: TaskQueue,
            scheduleOptions: new ScheduleOptions { TriggerImmediately = true },
            cancellationToken: ct).ConfigureAwait(false);

        try
        {
            _logger.LogInformation("Schedule '{ScheduleId}' created. Waiting 15s for repeated ticks...", scheduleId);
            await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);

            var desc = await schedHandle.DescribeAsync().ConfigureAwait(false);
            var executionIds = desc.Info.RecentActions
                .Select(action => action.Action).OfType<ScheduleActionExecutionStartWorkflow>()
                .Select(action => action.WorkflowId).Distinct(StringComparer.Ordinal).ToArray();
            if (executionIds.Length < 2)
                throw new InvalidOperationException("The schedule did not start two distinct report executions.");

            _logger.LogInformation("Schedule actions so far: {Count}", desc.Info.NumActions);
            foreach (var id in executionIds) _logger.LogInformation("Report execution: {ObjectId}", id);
        }
        finally
        {
            await schedHandle.DeleteAsync().ConfigureAwait(false);
        }
    }

    private async Task RunSubscriptionTrackerDemoAsync(CancellationToken ct)
    {
        _logger.LogInformation("--- Pattern 2: Canonical Reminder (SubscriptionTracker) ---");
        _logger.LogInformation("Every tick is delivered to the SAME execution. State accumulates.");

        const string trackerObjectId = "subscription-tracker-demo";
        var scheduleId = $"subscription-reminder-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        // Ensure the tracker exists before creating the reminder schedule.
        var tracker = await _factory.GetOrCreateAsync<ISubscriptionTracker>(
            trackerObjectId, ct).ConfigureAwait(false);

        // Subscribe an email address so notifications fire on each reminder delivery.
        await tracker.SubscribeAsync("demo@example.com").ConfigureAwait(false);
        _logger.LogInformation("Subscribed demo@example.com to tracker '{ObjectId}'", trackerObjectId);

        var schedHandle = await _factory.CreateDurableObjectReminderAsync<ISubscriptionTracker>(
            scheduleId,
            targetObjectId: trackerObjectId,
            reminderName: "weekly-digest",
            new ScheduleSpec
            {
                // Short interval for demo purposes — 10 seconds between ticks.
                Intervals = [new ScheduleIntervalSpec(TimeSpan.FromSeconds(10))],
            },
            taskQueue: TaskQueue,
            scheduleOptions: new ScheduleOptions { TriggerImmediately = true },
            cancellationToken: ct).ConfigureAwait(false);

        try
        {
            _logger.LogInformation("Reminder schedule '{ScheduleId}' created. Waiting 15s for repeated deliveries...", scheduleId);
            await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);

            var count = tracker.GetReminderCount();
            if (count < 2) throw new InvalidOperationException("The tracker did not receive multiple reminders.");
            _logger.LogInformation("Reminder count on canonical execution: {Count}", count);
        }
        finally
        {
            await schedHandle.DeleteAsync().ConfigureAwait(false);
            await tracker.DeactivateAsync().ConfigureAwait(false);
        }
    }
}
