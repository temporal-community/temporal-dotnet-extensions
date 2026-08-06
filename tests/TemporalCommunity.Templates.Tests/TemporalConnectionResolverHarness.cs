using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Result of one <c>TemporalConnection.Resolve</c> scenario, as reported by the harness process.
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
/// Builds and runs a small harness console app that references the generated
/// <c>temporal-worker</c> project via <c>ProjectReference</c> and calls its
/// <c>TemporalConnection.Resolve</c> directly — the same "reference the generated project from a
/// separate harness assembly" mechanism the plan uses for its multi-project template's analogous
/// <c>SharedTemporalConnection</c> helper. A <c>ProjectReference</c> (rather than loading the built
/// assembly via reflection) guarantees the harness and the generated project share the exact same
/// <c>Temporalio.Client.TemporalClientConnectOptions</c> type identity, since both resolve
/// <c>Temporalio</c> through the same MSBuild build graph.
/// </summary>
internal static class TemporalConnectionResolverHarness
{
    /// <summary>
    /// Instantiates <c>temporal-worker</c> (default Framework/IncludeOtel — this template's
    /// TemporalConnection.cs content does not vary with either symbol), generates a harness project
    /// that references it, runs all five precedence scenarios in one process, and returns the
    /// parsed results.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, TemporalConnectionResolverResult>> RunAllScenariosAsync()
    {
        var targetDirectory = TestFixtures.CreateTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        var harnessDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            const string targetName = "TemporalConnectionHarnessTarget";
            await TemporalWorkerTestHelper.InstantiateAsync(
                targetName, "net10.0", includeOtel: false, targetDirectory, settingsDirectory)
                .ConfigureAwait(false);

            var targetCsprojPath = Path.Combine(targetDirectory, $"{targetName}.csproj");
            Assert.True(File.Exists(targetCsprojPath));

            await File.WriteAllTextAsync(
                Path.Combine(harnessDirectory, "Harness.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
                  </ItemGroup>
                  <ItemGroup>
                    <ProjectReference Include="{targetCsprojPath}" />
                  </ItemGroup>
                </Project>
                """).ConfigureAwait(false);

            await File.WriteAllTextAsync(
                Path.Combine(harnessDirectory, "Program.cs"),
                BuildHarnessProgramSource(targetName)).ConfigureAwait(false);

            await DotnetCli.RunAsync(harnessDirectory, "build", "--nologo", "-c", "Debug").ConfigureAwait(false);

            var harnessDllPath = Path.Combine(harnessDirectory, "bin", "Debug", "net10.0", "Harness.dll");
            Assert.True(File.Exists(harnessDllPath), $"Expected built harness at '{harnessDllPath}'.");

            var stdOut = await DotnetCli.RunAsync(harnessDirectory, harnessDllPath).ConfigureAwait(false);
            return ParseResults(stdOut);
        }
        finally
        {
            TestFixtures.DeleteDirectory(targetDirectory);
            TestFixtures.DeleteDirectory(settingsDirectory);
            TestFixtures.DeleteDirectory(harnessDirectory);
        }
    }

    private static Dictionary<string, TemporalConnectionResolverResult> ParseResults(string stdOut)
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

    private static string BuildHarnessProgramSource(string targetNamespace) =>
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
            return ("EnvWinsOverConfig", {{targetNamespace}}.TemporalConnection.Resolve(configuration, profileLoadOptions));
        }

        static (string Scenario, TemporalClientConnectOptions Options) Scenario2ConfigWinsOverDefault()
        {
            var configuration = BuildConfiguration(("Temporal:Address", "configured-address:7233"));
            var profileLoadOptions = new ClientEnvConfig.ProfileLoadOptions
            {
                DisableFile = true,
                OverrideEnvVars = new Dictionary<string, string>(),
            };
            return ("ConfigWinsOverDefault", {{targetNamespace}}.TemporalConnection.Resolve(configuration, profileLoadOptions));
        }

        static (string Scenario, TemporalClientConnectOptions Options) Scenario3NeitherPresentDefaultsToLocalhost()
        {
            var configuration = BuildConfiguration();
            var profileLoadOptions = new ClientEnvConfig.ProfileLoadOptions
            {
                DisableFile = true,
                OverrideEnvVars = new Dictionary<string, string>(),
            };
            return ("NeitherPresentDefaultsToLocalhost", {{targetNamespace}}.TemporalConnection.Resolve(configuration, profileLoadOptions));
        }

        static (string Scenario, TemporalClientConnectOptions Options) Scenario4BlankConfigFallsThroughToLocalhost()
        {
            var configuration = BuildConfiguration(("Temporal:Address", "   "));
            var profileLoadOptions = new ClientEnvConfig.ProfileLoadOptions
            {
                DisableFile = true,
                OverrideEnvVars = new Dictionary<string, string>(),
            };
            return ("BlankConfigFallsThroughToLocalhost", {{targetNamespace}}.TemporalConnection.Resolve(configuration, profileLoadOptions));
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
            return ("ProfilePropertiesSurvive", {{targetNamespace}}.TemporalConnection.Resolve(configuration, profileLoadOptions));
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
