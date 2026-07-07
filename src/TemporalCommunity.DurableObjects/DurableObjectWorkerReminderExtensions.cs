using Temporalio.Extensions.Hosting;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Extension methods for registering reminder delivery activities on a DurableObject hosted worker.
/// </summary>
/// <remarks>
/// These extensions are kept in a separate file from <c>DurableObjectWorkerExtensions.cs</c>
/// (Phase 3) to avoid file conflicts during parallel phase development. Both files contribute to
/// the same conceptual worker setup surface.
/// </remarks>
public static class DurableObjectWorkerReminderExtensions
{
    /// <summary>
    /// Registers a <see cref="ReminderDeliveryActivities"/> instance on this hosted worker for
    /// reminder delivery.
    /// </summary>
    /// <remarks>
    /// Use this overload when you have already constructed the activities instance (e.g., in a
    /// non-DI scenario, or when you want to supply a custom <see cref="Temporalio.Client.ITemporalClient"/>
    /// for delivery, separate from the main client). Workers that do not use reminders can skip
    /// this call entirely.
    /// </remarks>
    /// <param name="builder">The worker service options builder to configure.</param>
    /// <param name="activities">The activities instance to register.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static ITemporalWorkerServiceOptionsBuilder AddDurableObjectReminderDelivery(
        this ITemporalWorkerServiceOptionsBuilder builder,
        ReminderDeliveryActivities activities) =>
        builder.ConfigureOptions(opts => opts.AddAllActivities(activities));

    /// <summary>
    /// Registers <see cref="ReminderDeliveryActivities"/> from the DI container on this hosted
    /// worker for reminder delivery. The container resolves the instance at worker startup.
    /// <typeparamref name="TActivities"/> must be registered as a service before the worker host
    /// starts.
    /// </summary>
    /// <remarks>
    /// Use this overload when <typeparamref name="TActivities"/> is registered in the DI container.
    /// The container resolves the instance at worker startup via <c>PostConfigure</c> —
    /// <typeparamref name="TActivities"/> must be registered as a singleton or scoped service in DI.
    /// </remarks>
    /// <typeparam name="TActivities">
    /// A type registered in DI that carries activity methods. Typically
    /// <see cref="ReminderDeliveryActivities"/> itself or a subclass created for testing.
    /// </typeparam>
    /// <param name="builder">The worker service options builder to configure.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public static ITemporalWorkerServiceOptionsBuilder AddDurableObjectReminderDelivery<TActivities>(
        this ITemporalWorkerServiceOptionsBuilder builder)
        where TActivities : class =>
        builder.ApplyTemporalActivities<TActivities>();
}
