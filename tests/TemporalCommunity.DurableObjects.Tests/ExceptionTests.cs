using TemporalCommunity.DurableObjects;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

public sealed class ExceptionTests
{
    // DurableObjectNotFoundException: message contains objectId.
    [Fact]
    public void DurableObjectNotFoundException_MessageContainsObjectId()
    {
        var ex = new DurableObjectNotFoundException("obj-123", null);
        Assert.Contains("obj-123", ex.Message, StringComparison.Ordinal);
    }

    // DurableObjectNotFoundException: inner exception is preserved.
    [Fact]
    public void DurableObjectNotFoundException_StoresInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new DurableObjectNotFoundException("obj-123", inner);
        Assert.Same(inner, ex.InnerException);
    }

    // DurableObjectNotFoundException: ObjectId property holds the ID.
    [Fact]
    public void DurableObjectNotFoundException_StoresObjectId()
    {
        var ex = new DurableObjectNotFoundException("obj-123", null);
        Assert.Equal("obj-123", ex.ObjectId);
    }

    // DurableObjectNotActiveException: message contains objectId.
    [Fact]
    public void DurableObjectNotActiveException_MessageContainsObjectId()
    {
        var ex = new DurableObjectNotActiveException("obj-456");
        Assert.Contains("obj-456", ex.Message, StringComparison.Ordinal);
    }

    // DurableObjectNotActiveException: inner exception is preserved.
    [Fact]
    public void DurableObjectNotActiveException_StoresInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new DurableObjectNotActiveException("msg", inner);
        Assert.Same(inner, ex.InnerException);
    }

    // Both exceptions derive from DurableObjectException.
    [Fact]
    public void BothExceptions_DeriveFromDurableObjectException()
    {
        var notFound = new DurableObjectNotFoundException("x", null);
        var notActive = new DurableObjectNotActiveException("y");

        Assert.IsAssignableFrom<DurableObjectException>(notFound);
        Assert.IsAssignableFrom<DurableObjectException>(notActive);
    }

    // DurableObjectException is itself an Exception.
    [Fact]
    public void DurableObjectException_DerivesFromException()
    {
        var notFound = new DurableObjectNotFoundException("x", null);
        Assert.IsAssignableFrom<Exception>(notFound);
    }
}
