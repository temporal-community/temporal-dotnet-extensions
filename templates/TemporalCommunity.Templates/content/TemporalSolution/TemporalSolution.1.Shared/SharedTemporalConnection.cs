using Microsoft.Extensions.Configuration;
using Temporalio.Client;
using Temporalio.Common.EnvConfig;

namespace GeneratedNamespacePrefix.Shared;

/// <summary>
/// Resolves the Temporal connection settings shared by the Worker and Client.
/// Precedence: environment/profile, <c>Temporal:Address</c>, then <c>localhost:7233</c>.
/// </summary>
public static class SharedTemporalConnection
{
    /// <summary>
    /// Loads environment/profile settings first. If no target host is present, uses
    /// <c>Temporal:Address</c> or <c>localhost:7233</c>. Other environment/profile settings are
    /// preserved.
    /// </summary>
    /// <param name="configuration">Configuration providing the fallback "Temporal:Address" value.</param>
    /// <param name="profileLoadOptions">
    /// Options for loading environment/profile settings. The default uses the current environment.
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
    /// Copies the resolved connection settings into SDK-managed client options.
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
