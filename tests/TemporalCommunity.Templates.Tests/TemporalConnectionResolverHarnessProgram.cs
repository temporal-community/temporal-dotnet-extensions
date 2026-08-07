using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Result of one connection-resolver scenario, as reported by a harness process.
/// </summary>
internal sealed record TemporalConnectionResolverResult(
    string Scenario,
    string TargetHost,
    string Namespace,
    string ApiKey,
    string TlsServerName,
    int RpcMetadataCount,
    string RpcMetadataFirst);

/// <summary>
/// Shared harness program source and output parsing for the five connection-resolver precedence
/// scenarios, reused by both <see cref="TemporalConnectionResolverHarness"/> (temporal-worker's own
/// <c>TemporalConnection.Resolve</c>) and <see cref="SharedTemporalConnectionResolverHarness"/>
/// (temporal-solution's <c>SharedTemporalConnection.Resolve</c>) — the two are separate
/// implementations of the same design (no shared assembly between the single-project and
/// multi-project templates), so both need coverage, but the scenario logic itself is identical.
/// </summary>
internal static class TemporalConnectionResolverHarnessProgram
{
    public static Dictionary<string, TemporalConnectionResolverResult> ParseResults(string stdOut)
    {
        var results = new Dictionary<string, TemporalConnectionResolverResult>(StringComparer.Ordinal);
        foreach (var line in stdOut.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("RESULT|", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('|');
            Assert.Equal(8, parts.Length);
            var result = new TemporalConnectionResolverResult(
                Scenario: parts[1],
                TargetHost: parts[2],
                Namespace: parts[3],
                ApiKey: parts[4],
                TlsServerName: parts[5],
                RpcMetadataCount: int.Parse(parts[6], System.Globalization.CultureInfo.InvariantCulture),
                RpcMetadataFirst: parts.Length > 7 ? parts[7] : "<none>");
            results[result.Scenario] = result;
        }

        return results;
    }

    /// <param name="resolverExpression">
    /// Fully qualified static method expression, e.g. <c>"MyNamespace.TemporalConnection.Resolve"</c>.
    /// </param>
    public static string Build(string resolverExpression) =>
        $$""""
        using System.Globalization;
        using System.Linq;
        using Microsoft.Extensions.Configuration;
        using Temporalio.Client;
        using Temporalio.Common.EnvConfig;

        Print(Scenario1EnvWinsOverConfig());
        Print(Scenario2ConfigWinsOverDefault());
        Print(Scenario3NeitherPresentDefaultsToLocalhost());
        Print(Scenario4BlankConfigFallsThroughToLocalhost());
        Print(Scenario5ProfilePropertiesSurvive());

        static (string Scenario, TemporalClientConnectOptions Options) Scenario1EnvWinsOverConfig()
        {
            var configuration = BuildConfiguration(("Temporal:Address", "should-be-ignored:9999"));
            var profileLoadOptions = new ClientEnvConfig.ProfileLoadOptions
            {
                DisableFile = true,
                OverrideEnvVars = new Dictionary<string, string> { ["TEMPORAL_ADDRESS"] = "env-address:7233" },
            };
            return ("EnvWinsOverConfig", {{resolverExpression}}(configuration, profileLoadOptions));
        }

        static (string Scenario, TemporalClientConnectOptions Options) Scenario2ConfigWinsOverDefault()
        {
            var configuration = BuildConfiguration(("Temporal:Address", "configured-address:7233"));
            var profileLoadOptions = new ClientEnvConfig.ProfileLoadOptions
            {
                DisableFile = true,
                OverrideEnvVars = new Dictionary<string, string>(),
            };
            return ("ConfigWinsOverDefault", {{resolverExpression}}(configuration, profileLoadOptions));
        }

        static (string Scenario, TemporalClientConnectOptions Options) Scenario3NeitherPresentDefaultsToLocalhost()
        {
            var configuration = BuildConfiguration();
            var profileLoadOptions = new ClientEnvConfig.ProfileLoadOptions
            {
                DisableFile = true,
                OverrideEnvVars = new Dictionary<string, string>(),
            };
            return ("NeitherPresentDefaultsToLocalhost", {{resolverExpression}}(configuration, profileLoadOptions));
        }

        static (string Scenario, TemporalClientConnectOptions Options) Scenario4BlankConfigFallsThroughToLocalhost()
        {
            var configuration = BuildConfiguration(("Temporal:Address", "   "));
            var profileLoadOptions = new ClientEnvConfig.ProfileLoadOptions
            {
                DisableFile = true,
                OverrideEnvVars = new Dictionary<string, string>(),
            };
            return ("BlankConfigFallsThroughToLocalhost", {{resolverExpression}}(configuration, profileLoadOptions));
        }

        static (string Scenario, TemporalClientConnectOptions Options) Scenario5ProfilePropertiesSurvive()
        {
            const string toml = """
                [profile.default]
                address = "profile-address:7233"
                namespace = "profile-namespace"
                api_key = "profile-api-key"
                [profile.default.tls]
                server_name = "profile-server-name"
                [profile.default.grpc_meta]
                x-custom-header = "profile-header-value"
                """;
            var configuration = BuildConfiguration();
            var profileLoadOptions = new ClientEnvConfig.ProfileLoadOptions
            {
                ConfigSource = DataSource.FromUTF8String(toml),
                OverrideEnvVars = new Dictionary<string, string>(),
            };
            return ("ProfilePropertiesSurvive", {{resolverExpression}}(configuration, profileLoadOptions));
        }

        static IConfiguration BuildConfiguration(params (string Key, string Value)[] entries)
        {
            var values = new Dictionary<string, string?>();
            foreach (var (key, value) in entries)
            {
                values[key] = value;
            }

            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        static void Print((string Scenario, TemporalClientConnectOptions Options) scenario)
        {
            var options = scenario.Options;
            var tlsServerName = options.Tls?.Domain ?? "<null>";
            var rpcMetadataCount = options.RpcMetadata?.Count ?? 0;
            var rpcMetadataFirst = "<none>";
            if (options.RpcMetadata is { Count: > 0 })
            {
                var first = options.RpcMetadata.First();
                rpcMetadataFirst = $"{first.Key}={first.Value}";
            }

            Console.WriteLine(string.Join(
                '|',
                "RESULT",
                scenario.Scenario,
                options.TargetHost ?? "<null>",
                options.Namespace,
                options.ApiKey ?? "<null>",
                tlsServerName,
                rpcMetadataCount.ToString(CultureInfo.InvariantCulture),
                rpcMetadataFirst));
        }
        """";
}
