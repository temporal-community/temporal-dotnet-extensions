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
/// <c>TemporalWorkerConnection.Resolve</c>) and <see cref="SharedTemporalConnectionResolverHarness"/>
/// (temporal-solution's <c>SharedTemporalConnection.Resolve</c>) — the two are separate
/// implementations of the same design (no shared assembly between the single-project and
/// multi-project templates), so both need coverage, but the scenario logic itself is identical.
/// </summary>
/// <summary>
/// Parsed harness output: the resolver precedence scenarios plus the <c>ApplyTo</c> registration
/// scenario.
/// </summary>
internal sealed record TemporalConnectionResolverRun(
    IReadOnlyDictionary<string, TemporalConnectionResolverResult> Scenarios,
    IReadOnlyDictionary<string, string> ApplyTo);

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

    /// <summary>
    /// Parses the <c>APPLY|key|value</c> lines emitted by the <c>ApplyTo</c> scenario, which runs the
    /// generated helper through the SDK's real <c>AddTemporalClient</c> options pipeline.
    /// </summary>
    public static Dictionary<string, string> ParseApplyResults(string stdOut)
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in stdOut.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("APPLY|", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('|', 3);
            Assert.Equal(3, parts.Length);
            results[parts[1]] = parts[2];
        }

        return results;
    }

    public static TemporalConnectionResolverRun ParseRun(string stdOut) =>
        new(ParseResults(stdOut), ParseApplyResults(stdOut));

    public static void AssertProfileApplyToContract(IReadOnlyDictionary<string, string> apply)
    {
        Assert.Equal("apply-host:7233", apply["TargetHost"]);
        Assert.Equal("apply-namespace", apply["Namespace"]);
        Assert.Equal("apply-domain", apply["TlsDomain"]);
        Assert.Equal("False", apply["TlsIsCopy"]);
        Assert.Equal("apply-api-key", apply["ApiKey"]);
        Assert.Equal("x-apply=text-value", apply["RpcMetadata"]);

        // Settings outside the environment/profile contract retain SDK defaults.
        Assert.Equal("<null>", apply["RpcRetryMaxRetries"]);
        Assert.Equal("False", apply["KeepAliveIsNull"]);
        Assert.Equal("False", apply["DataConverterIsResolved"]);
        Assert.Equal("<null>", apply["QueryRejectCondition"]);
        Assert.Equal("False", apply["PluginsAreResolved"]);
        Assert.Equal("tracing", apply["Interceptors"]);
        Assert.Equal("True", apply["LoggerFactoryIsHost"]);
        Assert.Equal("True", apply["ResolvedUnchanged"]);
        Assert.Equal("True", apply["ClientLoggerFactoryIsHost"]);
        Assert.Equal("1", apply["ClientInterceptorCount"]);
        Assert.Equal("apply-namespace", apply["ClientNamespace"]);
        Assert.Equal("apply-host:7233", apply["ClientTargetHost"]);
        Assert.Equal("False", apply["ClientIsConnected"]);
    }

    /// <summary>
    /// The exact TracingInterceptor composition expression the generated Program.cs files use when
    /// IncludeOtel is enabled. The harness runs the same expression with a stand-in interceptor so it
    /// does not need the OpenTelemetry packages.
    /// </summary>
    public const string OtelInterceptorComposition =
        "options.Interceptors = new[] { new TracingInterceptor() };";

    /// <param name="resolverExpression">
    /// Fully qualified static method expression, e.g. <c>"MyNamespace.TemporalWorkerConnection.Resolve"</c>.
    /// </param>
    /// <param name="applyToExpression">
    /// Fully qualified static method expression, e.g. <c>"MyNamespace.TemporalWorkerConnection.ApplyTo"</c>.
    /// </param>
    public static string Build(string resolverExpression, string applyToExpression) =>
        $$""""
        using System.Globalization;
        using System.Linq;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Logging;
        using Microsoft.Extensions.Options;
        using Temporalio.Api.Enums.V1;
        using Temporalio.Client;
        using Temporalio.Client.Interceptors;
        using Temporalio.Common;
        using Temporalio.Common.EnvConfig;
        using Temporalio.Converters;
        using Temporalio.Runtime;

        Print(Scenario1EnvWinsOverConfig());
        Print(Scenario2ConfigWinsOverDefault());
        Print(Scenario3NeitherPresentDefaultsToLocalhost());
        Print(Scenario4BlankConfigFallsThroughToLocalhost());
        Print(Scenario5ProfilePropertiesSurvive());
        RunApplyToScenario();

        // Mirrors the generated Program.cs registration: AddTemporalClient + ApplyTo, plus the
        // IncludeOtel interceptor composition (with a stand-in for TracingInterceptor).
        static void RunApplyToScenario()
        {
            var existingInterceptor = new MarkerInterceptor();
            var tracingStandIn = new MarkerInterceptor();
            var plugin = new SimplePlugin("apply-plugin");
            using var resolvedLoggerFactory = new LoggerFactory();
            using var hostLoggerFactory = new LoggerFactory();
            var dataConverter = DataConverter.Default with { };
            var runtime = TemporalRuntime.Default;
            var resolved = new TemporalClientConnectOptions("apply-host:7233")
            {
                Tls = new TlsOptions { Domain = "apply-domain" },
                RpcRetry = new RpcRetryOptions { MaxRetries = 7 },
                KeepAlive = null,
                HttpConnectProxy = new HttpConnectProxyOptions("proxy-host:8080"),
                DnsLoadBalancing = new DnsLoadBalancingOptions { ResolutionInterval = TimeSpan.FromSeconds(42) },
                PayloadLimits = new PayloadLimitsOptions { PayloadsWarnSize = 12345 },
                GrpcCompression = new GrpcCompression.None(),
                RpcMetadata = new List<KeyValuePair<string, string>> { new("x-apply", "text-value") },
                RpcBinaryMetadata = new List<KeyValuePair<string, byte[]>> { new("x-apply-bin", new byte[] { 1, 2, 3 }) },
                ApiKey = "apply-api-key",
                Identity = "apply-identity",
                Runtime = runtime,
                Namespace = "apply-namespace",
                DataConverter = dataConverter,
                Interceptors = new IClientInterceptor[] { existingInterceptor },
                LoggerFactory = resolvedLoggerFactory,
                QueryRejectCondition = QueryRejectCondition.NotOpen,
                Plugins = new ITemporalClientPlugin[] { plugin },
            };

            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(hostLoggerFactory);
            services.AddTemporalClient(options =>
            {
                {{applyToExpression}}(resolved, options);
                {{OtelInterceptorComposition.Replace("new TracingInterceptor()", "tracingStandIn", StringComparison.Ordinal)}}
            });
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<TemporalClientConnectOptions>>().Value;
            var client = provider.GetRequiredService<ITemporalClient>();

            Apply("TargetHost", options.TargetHost);
            Apply("TlsDomain", options.Tls?.Domain);
            Apply("TlsIsCopy", options.Tls is not null && !ReferenceEquals(options.Tls, resolved.Tls));
            Apply("RpcRetryMaxRetries", options.RpcRetry?.MaxRetries);
            Apply("RpcRetryIsCopy", options.RpcRetry is not null && !ReferenceEquals(options.RpcRetry, resolved.RpcRetry));
            Apply("KeepAliveIsNull", options.KeepAlive is null);
            Apply("HttpConnectProxyTargetHost", options.HttpConnectProxy?.TargetHost);
            Apply("DnsResolutionSeconds", options.DnsLoadBalancing?.ResolutionInterval.TotalSeconds);
            Apply("DnsIsCopy", options.DnsLoadBalancing is not null && !ReferenceEquals(options.DnsLoadBalancing, resolved.DnsLoadBalancing));
            Apply("PayloadsWarnSize", options.PayloadLimits?.PayloadsWarnSize);
            Apply("PayloadLimitsIsCopy", options.PayloadLimits is not null && !ReferenceEquals(options.PayloadLimits, resolved.PayloadLimits));
            Apply("GrpcCompressionIsNone", options.GrpcCompression is GrpcCompression.None);
            Apply("RpcMetadata", string.Join(",", (options.RpcMetadata ?? []).Select(kv => $"{kv.Key}={kv.Value}")));
            Apply("RpcBinaryMetadata", string.Join(",", (options.RpcBinaryMetadata ?? []).Select(kv => $"{kv.Key}={Convert.ToHexString(kv.Value)}")));
            Apply("ApiKey", options.ApiKey);
            Apply("Identity", options.Identity);
            Apply("RuntimeIsResolved", ReferenceEquals(options.Runtime, runtime));
            Apply("Namespace", options.Namespace);
            Apply("DataConverterIsResolved", ReferenceEquals(options.DataConverter, dataConverter) && !ReferenceEquals(dataConverter, DataConverter.Default));
            Apply("QueryRejectCondition", options.QueryRejectCondition);
            Apply("PluginsAreResolved", options.Plugins is { Count: 1 } && ReferenceEquals(options.Plugins.First(), plugin));
            Apply("Interceptors", string.Join(",", (options.Interceptors ?? []).Select(i =>
                ReferenceEquals(i, existingInterceptor) ? "existing" : ReferenceEquals(i, tracingStandIn) ? "tracing" : "other")));
            Apply("LoggerFactoryIsHost", ReferenceEquals(options.LoggerFactory, hostLoggerFactory));
            Apply("ResolvedUnchanged",
                resolved.Interceptors.Count == 1 && ReferenceEquals(resolved.LoggerFactory, resolvedLoggerFactory));
            Apply("ClientLoggerFactoryIsHost", ReferenceEquals(client.Options.LoggerFactory, hostLoggerFactory));
            Apply("ClientInterceptorCount", client.Options.Interceptors?.Count ?? 0);
            Apply("ClientNamespace", client.Options.Namespace);
            Apply("ClientTargetHost", client.Connection.Options.TargetHost);
            Apply("ClientIsConnected", client.Connection.IsConnected);
        }

        static void Apply(string key, object? value) =>
            Console.WriteLine($"APPLY|{key}|{(value is null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture))}");

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

        sealed class MarkerInterceptor : IClientInterceptor
        {
        }
        """";
}
