using System.Collections.Concurrent;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Registry populated by generated module initializers so factories can create concrete clients.
/// </summary>
public static class DurableObjectGeneratedClientRegistry
{
    private static readonly ConcurrentDictionary<Type, Func<DurableObjectClientInvoker, object>>
        s_factories = new();

    /// <summary>Registers a generated implementation for a DurableObject contract.</summary>
    public static void Register<TContract>(Func<DurableObjectClientInvoker, TContract> factory)
        where TContract : IDurableObject
    {
        Polyfills.Throw.IfNull(factory, nameof(factory));
        if (!s_factories.TryAdd(typeof(TContract), invoker => factory(invoker)!))
        {
            throw new InvalidOperationException(
                $"A generated client is already registered for '{typeof(TContract).FullName}'.");
        }
    }

    /// <summary>Returns whether a generated client registered for the contract.</summary>
    public static bool IsRegistered<TContract>() where TContract : IDurableObject =>
        s_factories.ContainsKey(typeof(TContract));

    internal static bool TryCreate<TContract>(
        DurableObjectClientInvoker invoker,
        out TContract? client)
        where TContract : IDurableObject
    {
        if (s_factories.TryGetValue(typeof(TContract), out var factory))
        {
            client = (TContract)factory(invoker);
            return true;
        }

        client = default;
        return false;
    }
}
