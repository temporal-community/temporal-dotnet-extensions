using TemporalCommunity.DurableObjects;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

/// <summary>
/// Static property checks for DurableObjectWorkerInterceptor. No live Temporal server needed.
/// Interceptor behavioral invariants (auth, serialization, drain-window, exception wrapping)
/// require live workflow context and are covered by Scenario N integration tests.
/// </summary>
public sealed class WorkerInterceptorTests
{
    // FrameworkUpdateNames contains exactly "OnReminder".
    [Fact]
    public void FrameworkUpdateNames_ContainsOnReminder()
    {
        // Cast to IEnumerable<string> to avoid ambiguity between Assert.Contains(ISet<T>) overloads.
        var names = (IEnumerable<string>)DurableObjectWorkerInterceptor.FrameworkUpdateNames;
        Assert.Contains("OnReminder", names);
    }

    // FrameworkUpdateNames does NOT contain "Deactivate" — it is user-initiated, not framework.
    [Fact]
    public void FrameworkUpdateNames_DoesNotContainDeactivate()
    {
        var names = (IEnumerable<string>)DurableObjectWorkerInterceptor.FrameworkUpdateNames;
        Assert.DoesNotContain("Deactivate", names);
    }

    // FrameworkUpdateNames contains exactly one entry.
    [Fact]
    public void FrameworkUpdateNames_ContainsExactlyOneEntry()
    {
        Assert.Single(DurableObjectWorkerInterceptor.FrameworkUpdateNames);
    }

    // DurableObjectWorkerOptions defaults: Serialize == true.
    [Fact]
    public void DurableObjectWorkerOptions_Default_SerializeIsTrue()
    {
        var opts = new DurableObjectWorkerOptions();
        Assert.True(opts.Serialize);
    }

    // DurableObjectWorkerOptions defaults: Authorize == null.
    [Fact]
    public void DurableObjectWorkerOptions_Default_AuthorizeIsNull()
    {
        var opts = new DurableObjectWorkerOptions();
        Assert.Null(opts.Authorize);
    }

    // Constructor overrides work: Serialize = false, Authorize set.
    [Fact]
    public void DurableObjectWorkerInterceptor_ConstructorAcceptsParameters()
    {
        bool AuthorizePredicate(Temporalio.Worker.Interceptors.HandleUpdateInput _) => true;

        // No exception on construction — verifies the constructor parameter types are correct.
        var interceptor = new DurableObjectWorkerInterceptor(
            serialize: false,
            authorize: AuthorizePredicate);
        Assert.NotNull(interceptor);
    }
}
