using Microsoft.Extensions.Configuration;
using Temporalio.Client;
using Temporalio.Common.EnvConfig;

namespace TemporalWorker1;

/// <summary>
/// Resolves the <see cref="TemporalClientConnectOptions"/> used to connect to a Temporal server,
/// following a fixed three-step precedence. See the "Connecting to Temporal" section of
/// docs/TEMPLATES.md for the full rationale.
/// </summary>
public static class TemporalWorkerConnection
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

    /// <summary>
    /// Copies every client connection setting from <paramref name="resolved"/> (typically the
    /// result of <see cref="Resolve"/>) onto <paramref name="target"/>, the DI-managed options
    /// instance passed to <c>AddTemporalClient</c>'s configure callback. That callback can only
    /// mutate its options instance, not replace it, so each setting is transferred explicitly.
    /// </summary>
    /// <remarks>
    /// <see cref="TemporalClientConnectOptions.LoggerFactory"/> is intentionally not copied:
    /// <c>AddTemporalClient</c> sets it to the host's <c>ILoggerFactory</c> before the callback runs.
    /// <paramref name="resolved"/> is cloned first so <paramref name="target"/> never shares the
    /// mutable TLS, RPC retry, keepalive, DNS load-balancing, or payload-limit option objects with it.
    /// </remarks>
    /// <param name="resolved">The resolved connection options to copy from.</param>
    /// <param name="target">The options instance to copy onto.</param>
    public static void ApplyTo(TemporalClientConnectOptions resolved, TemporalClientConnectOptions target)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(target);

        var source = (TemporalClientConnectOptions)resolved.Clone();

        // TemporalConnectionOptions
        target.TargetHost = source.TargetHost;
        target.Tls = source.Tls;
        target.RpcRetry = source.RpcRetry;
        target.KeepAlive = source.KeepAlive;
        target.HttpConnectProxy = source.HttpConnectProxy;
        target.DnsLoadBalancing = source.DnsLoadBalancing;
        target.GrpcCompression = source.GrpcCompression;
        target.PayloadLimits = source.PayloadLimits;
        target.RpcMetadata = source.RpcMetadata;
        target.RpcBinaryMetadata = source.RpcBinaryMetadata;
        target.ApiKey = source.ApiKey;
        target.Identity = source.Identity;
        target.Runtime = source.Runtime;

        // TemporalClientConnectOptions (LoggerFactory deliberately excluded, see remarks)
        target.Namespace = source.Namespace;
        target.DataConverter = source.DataConverter;
        target.Interceptors = source.Interceptors;
        target.QueryRejectCondition = source.QueryRejectCondition;
        target.Plugins = source.Plugins;
    }
}
