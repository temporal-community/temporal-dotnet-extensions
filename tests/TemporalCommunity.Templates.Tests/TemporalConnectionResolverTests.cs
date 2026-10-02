using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Focused coverage for <c>temporal-worker</c>'s own <c>TemporalWorkerConnection.Resolve</c> precedence
/// logic (env/profile -> "Temporal:Address" config -> localhost:7233), independent of the
/// Framework/IncludeOtel matrix covered in <see cref="TemporalWorkerTemplateTests"/>. All five
/// scenarios run in a single instantiation + harness build (see
/// <see cref="TemporalConnectionResolverHarness"/>) to keep the test fast.
/// </summary>
public sealed class TemporalConnectionResolverTests
{
    [Fact]
    public async Task ResolvePrecedencePropertyPreservationAndApplyToRegistration()
    {
        var run = await TemporalConnectionResolverHarness.RunAllScenariosAsync();
        var results = run.Scenarios;

        Assert.Equal(5, results.Count);

        // (1) Environment/profile TargetHost wins — "Temporal:Address" config is never consulted.
        var envWins = results["EnvWinsOverConfig"];
        Assert.Equal("env-address:7233", envWins.TargetHost);

        // (2) No env/profile TargetHost, but "Temporal:Address" is configured -> that wins over
        // the localhost:7233 default.
        var configWins = results["ConfigWinsOverDefault"];
        Assert.Equal("configured-address:7233", configWins.TargetHost);

        // (3) Neither present -> localhost:7233.
        var neitherPresent = results["NeitherPresentDefaultsToLocalhost"];
        Assert.Equal("localhost:7233", neitherPresent.TargetHost);

        // (4) The IsNullOrWhiteSpace boundary: a present-but-blank "Temporal:Address" value still
        // falls through to localhost:7233, proving the fix is not a bare "??"/IsNullOrEmpty check.
        var blankConfig = results["BlankConfigFallsThroughToLocalhost"];
        Assert.Equal("localhost:7233", blankConfig.TargetHost);

        // (5) A profile supplying Namespace/TLS/API key/RPC metadata survives unchanged into the
        // final options. Resolve() never touches these fields — only TargetHost is ever
        // conditionally overwritten — so this holds regardless of which TargetHost branch fired;
        // this scenario exercises it under the profile-supplies-TargetHost branch, since Resolve's
        // implementation makes no distinction between branches when copying the other fields.
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
