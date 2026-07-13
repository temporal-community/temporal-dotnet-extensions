using TemporalCommunity.DurableObjects;
using TemporalCommunity.DurableObjects.AotSmoke;

if (!DurableObjectGeneratedClientRegistry.IsRegistered<IAotCounter>())
{
    throw new InvalidOperationException("Generated IAotCounter client was not registered.");
}

if (typeof(AotCounterDurableObjectClient).Assembly.IsDynamic)
{
    throw new InvalidOperationException("Generated client must be a compile-time concrete type.");
}

Console.WriteLine("Generated DurableObject client registered under NativeAOT.");
