using Microsoft.Extensions.Configuration;
using Temporalio.Client;
using Temporalio.Common.EnvConfig;

namespace TemporalWorker1;

/// <summary>
/// Resolves the <see cref="TemporalClientConnectOptions"/> used to connect to a Temporal server,
/// following a fixed three-step precedence. See the "Connecting to Temporal" section of
/// docs/TEMPLATES.md for the full rationale.
/// </summary>
public static class TemporalConnection
{
    /// <summary>
    /// Resolves connection options using this precedence:
    /// <list type="number">
    /// <item><description>
    /// Environment variables (e.g. <c>TEMPORAL_ADDRESS</c>) or a Temporal CLI profile, via
    /// <see cref="ClientEnvConfig.LoadClientConnectOptions"/>. If this already supplies a
    /// <see cref="Temporalio.Client.TemporalConnectionOptions.TargetHost"/>, it wins and
    /// <c>Temporal:Address</c> below is never consulted.
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
}
