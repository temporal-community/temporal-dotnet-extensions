using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Extension methods for registering the DurableObject client-side factory in an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class DurableObjectServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDurableObjectFactory"/> as a singleton backed by the
    /// <see cref="ITemporalClient"/> already present in the container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="defaultTaskQueue"/> must match the task queue your DurableObject workers
    /// poll. It is set on the factory and used by all single-argument
    /// <c>Get&lt;T&gt;(objectId)</c> and <c>GetOrCreateAsync&lt;T&gt;(objectId)</c> calls;
    /// two-argument overloads allow per-call task-queue override.
    /// </para>
    /// <para>
    /// Requiring an explicit task queue at registration — rather than a library default — ensures
    /// misconfiguration is caught at startup rather than at the first RPC call.
    /// </para>
    /// <para>
    /// <b>Usage note:</b> <c>AddDurableObjects</c> (client-side factory registration) is
    /// intentionally distinct from <c>AddDurableObjectWorkflows</c> (worker-side type
    /// registration). Both are usually called in the same setup, but they serve different roles.
    /// See the README getting-started section for a side-by-side example.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="defaultTaskQueue">
    /// The default task queue used when no per-call task queue is specified.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="defaultTaskQueue"/> is null or empty.
    /// </exception>
    public static IServiceCollection AddDurableObjects(
        this IServiceCollection services,
        string defaultTaskQueue)
    {
        ArgumentException.ThrowIfNullOrEmpty(defaultTaskQueue);

        services.AddSingleton<IDurableObjectFactory>(sp =>
            new DurableObjectFactory(
                sp.GetRequiredService<ITemporalClient>(),
                defaultTaskQueue));

        return services;
    }
}
