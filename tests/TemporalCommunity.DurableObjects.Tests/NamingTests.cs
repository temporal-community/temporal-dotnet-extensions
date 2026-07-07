using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

public sealed class NamingTests
{
    // Interface with explicit [Workflow("MyName")] wins regardless of naming convention.
    [Fact]
    public void ExplicitWorkflowName_ReturnsExplicitName()
    {
        var result = DurableObjectNaming.ResolveWorkflowType(typeof(IExplicitlyNamed));
        Assert.Equal("MyName", result);
    }

    // ICounter → "Counter": leading I stripped when next char is uppercase.
    [Fact]
    public void ICounter_StripsLeadingI()
    {
        var result = DurableObjectNaming.ResolveWorkflowType(typeof(ICounterForNaming));
        Assert.Equal("CounterForNaming", result);
    }

    // IHTTPServer → "HTTPServer": leading I stripped even when next char starts an acronym.
    [Fact]
    public void IHTTPServer_StripsLeadingI()
    {
        var result = DurableObjectNaming.ResolveWorkflowType(typeof(IHTTPServerForNaming));
        Assert.Equal("HTTPServerForNaming", result);
    }

    // Iinterface → "Iinterface": NOT stripped because next char is lowercase.
    [Fact]
    public void Lowercase_NextChar_DoesNotStrip()
    {
        var result = DurableObjectNaming.ResolveWorkflowType(typeof(IinterfaceForNaming));
        Assert.Equal("IinterfaceForNaming", result);
    }

    // Counter (no leading I) → "Counter": no stripping needed.
    [Fact]
    public void NoLeadingI_ReturnsTypeName()
    {
        var result = DurableObjectNaming.ResolveWorkflowType(typeof(CounterNoI));
        Assert.Equal("CounterNoI", result);
    }

    // Call twice — cache returns the same value from ConcurrentDictionary.
    [Fact]
    public void Cache_ReturnsSameValue_OnSecondCall()
    {
        var first = DurableObjectNaming.ResolveWorkflowType(typeof(ICachedForNaming));
        var second = DurableObjectNaming.ResolveWorkflowType(typeof(ICachedForNaming));
        Assert.Equal(first, second);
    }

    // --- private test types: no need to redeclare DeactivateAsync — IDurableObject has it ---

    [Workflow("MyName")]
    private interface IExplicitlyNamed : IDurableObject { }

    [Workflow]
    private interface ICounterForNaming : IDurableObject { }

    [Workflow]
    private interface IHTTPServerForNaming : IDurableObject { }

    [Workflow]
    private interface IinterfaceForNaming : IDurableObject { }

    [Workflow]
    private interface CounterNoI : IDurableObject { }

    [Workflow]
    private interface ICachedForNaming : IDurableObject { }
}
