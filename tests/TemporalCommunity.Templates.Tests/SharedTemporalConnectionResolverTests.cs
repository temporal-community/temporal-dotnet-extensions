using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Focused coverage for <c>temporal-solution</c>'s own <c>SharedTemporalConnection.Resolve</c>
/// precedence logic — a separate implementation from <c>temporal-worker</c>'s (no shared assembly
/// between the single-project and multi-project templates), so it needs its own coverage rather
/// than relying on <see cref="TemporalConnectionResolverTests"/>. Same five scenarios, same
/// hermetic approach (explicit <c>ProfileLoadOptions</c>, never the process's real environment).
/// </summary>
public sealed class SharedTemporalConnectionResolverTests
{
    [Fact]
    public async Task ResolvePrecedencePropertyPreservationAndApplyToRegistration()
    {
        var run = await SharedTemporalConnectionResolverHarness.RunAllScenariosAsync();
        var results = run.Scenarios;

        Assert.Equal(5, results.Count);

        var envWins = results["EnvWinsOverConfig"];
        Assert.Equal("env-address:7233", envWins.TargetHost);

        var configWins = results["ConfigWinsOverDefault"];
        Assert.Equal("configured-address:7233", configWins.TargetHost);

        var neitherPresent = results["NeitherPresentDefaultsToLocalhost"];
        Assert.Equal("localhost:7233", neitherPresent.TargetHost);

        var blankConfig = results["BlankConfigFallsThroughToLocalhost"];
        Assert.Equal("localhost:7233", blankConfig.TargetHost);

        var profileSurvives = results["ProfilePropertiesSurvive"];
        Assert.Equal("profile-address:7233", profileSurvives.TargetHost);
        Assert.Equal("profile-namespace", profileSurvives.Namespace);
        Assert.Equal("profile-api-key", profileSurvives.ApiKey);
        Assert.Equal("profile-server-name", profileSurvives.TlsServerName);
        Assert.Equal(1, profileSurvives.RpcMetadataCount);
        Assert.Equal("x-custom-header=profile-header-value", profileSurvives.RpcMetadataFirst);

        // (6) ApplyTo, run through the SDK's real AddTemporalClient options pipeline exactly as the
        // generated Program.cs files use it, transfers every resolved setting while keeping the
        // host ILoggerFactory and appending the tracing interceptor after existing ones.
        TemporalConnectionResolverHarnessProgram.AssertApplyToContract(run.ApplyTo);
    }
}
