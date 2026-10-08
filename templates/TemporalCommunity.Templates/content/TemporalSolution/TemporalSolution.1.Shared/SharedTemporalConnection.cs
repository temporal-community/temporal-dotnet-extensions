using Microsoft.Extensions.Configuration;
using Temporalio.Client;
using Temporalio.Common.EnvConfig;

namespace GeneratedNamespacePrefix.Shared;

/// <summary>
/// Resolves the <see cref="TemporalClientConnectOptions"/> used to connect to a Temporal server,
/// following a fixed three-step precedence. See the "Connecting to Temporal" section of
/// docs/templates.md for the full rationale. Both Worker and Client call this same helper so they
/// never end up pointed at different servers.
/// </summary>
public static class SharedTemporalConnection
{
    /// <summary>
    /// Resolves connection options using this precedence:
    /// <list type="number">
    /// <item><description>
    /// Environment variables (e.g. <c>TEMPORAL_ADDRESS</c> — this is what
    /// <c>AddTemporalLocalDevServer</c>'s <c>WithReference</c> injects when <c>IncludeAspire</c> is
    /// enabled) or a Temporal CLI profile, via <see cref="ClientEnvConfig.LoadClientConnectOptions"/>.
    /// If this already supplies a <see cref="Temporalio.Client.TemporalConnectionOptions.TargetHost"/>,
    /// it wins and <c>Temporal:Address</c> below is never consulted.
    /// </description></item>
    /// <item><description>
    /// Otherwise, the <c>Temporal:Address</c> configuration value (a present-but-blank value is
    /// treated the same as a missing one).
    /// </description></item>
    /// <item><description>
    /// Otherwise, <c>localhost:7233</c>.
    /// </description></item>
    /// </list>
    /// Every other property returned by step 1 (namespace, TLS, API key, RPC metadata) is
    /// preserved untouched — only <c>TargetHost</c> is ever conditionally overwritten. Temporal
    /// Cloud credentials, TLS settings, namespace, and RPC metadata always come from step 1, never
    /// from <c>Temporal:Address</c>, which only ever controls the target host.
    /// </summary>
    /// <param name="configuration">Configuration providing the fallback "Temporal:Address" value.</param>
    /// <param name="profileLoadOptions">
    /// Options for loading the environment/profile configuration in step 1. Pass <c>null</c> (the
    /// default) in production to use the real environment/profile; tests should pass an explicit
    /// <see cref="ClientEnvConfig.ProfileLoadOptions"/> (e.g. with <c>OverrideEnvVars</c>,
    /// <c>ConfigSource</c>, and <c>DisableFile</c> set) to stay hermetic.
    /// </param>
    /// <returns>The resolved connection options.</returns>
    public static TemporalClientConnectOptions Resolve(
        IConfiguration configuration,
        ClientEnvConfig.ProfileLoadOptions? profileLoadOptions = null)
    {
        var options = ClientEnvConfig.LoadClientConnectOptions(profileLoadOptions);
        if (string.IsNullOrWhiteSpace(options.TargetHost))
        {
            var configured = configuration["Temporal:Address"];
            options.TargetHost = string.IsNullOrWhiteSpace(configured) ? "localhost:7233" : configured;
        }

        return options;
    }

    /// <summary>
    /// Copies the resolved environment/profile settings into the SDK-managed client options.
    /// Preserves host logging and all other SDK settings.
    /// </summary>
    /// <param name="resolved">The resolved connection options to copy from.</param>
    /// <param name="target">The options instance to copy onto.</param>
    public static void ApplyTo(TemporalClientConnectOptions resolved, TemporalClientConnectOptions target)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(target);

        target.TargetHost = resolved.TargetHost;
        target.Namespace = resolved.Namespace;
        target.Tls = resolved.Tls;
        target.ApiKey = resolved.ApiKey;
        target.RpcMetadata = resolved.RpcMetadata;
    }
}
