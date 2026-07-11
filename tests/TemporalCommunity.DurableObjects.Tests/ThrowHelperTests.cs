using TemporalCommunity.DurableObjects.Polyfills;
using Xunit;

namespace TemporalCommunity.DurableObjects.Tests;

public sealed class ThrowHelperTests
{
    [Fact]
    public void IfNullOrEmpty_Null_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => Throw.IfNullOrEmpty(null, "taskQueue"));

        Assert.Equal("taskQueue", exception.ParamName);
    }

    [Fact]
    public void IfNullOrEmpty_Empty_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Throw.IfNullOrEmpty(string.Empty, "taskQueue"));

        Assert.Equal("taskQueue", exception.ParamName);
    }
}
