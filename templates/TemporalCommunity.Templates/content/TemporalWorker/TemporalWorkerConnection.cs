using Microsoft.Extensions.Configuration;
using Temporalio.Client;
using Temporalio.Common.EnvConfig;

namespace TemporalWorker1;

/// <summary>
/// Resolves the <see cref="TemporalClientConnectOptions"/> used to connect to a Temporal server,
/// following a fixed three-step precedence. See the "Connecting to Temporal" section of
/// docs/templates.md for the full rationale.
/// </summary>
public static class TemporalWorkerConnection
{
    /// <summary>
    /// Loads connection options from Temporal environment variables or a CLI profile.
    /// If the address is blank, uses <c>Temporal:Address</c>, then <c>localhost:7233</c>.
    /// All other connection settings are preserved.
    /// </summary>
    /// <param name="configuration">Configuration providing the fallback "Temporal:Address" value.</param>
    /// <param name="profileLoadOptions">
    /// Optional profile-loading settings; <c>null</c> uses the SDK defaults.
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
