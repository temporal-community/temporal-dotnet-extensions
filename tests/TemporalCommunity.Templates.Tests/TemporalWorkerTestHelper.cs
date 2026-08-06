using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.TemplateEngine.Authoring.TemplateVerifier;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Shared instantiation helper for the <c>temporal-worker</c> project template. Unlike the item
/// templates (which snapshot-diff a single generated file against a checked-in fixture),
/// <c>temporal-worker</c>'s tests assert directly on generated file content and on
/// <c>dotnet build</c> success — so <see cref="TemplateVerifierOptions.VerificationIncludePatterns"/>
/// is set to a pattern that matches no file, which skips the snapshot-diff step entirely while
/// still using TemplateVerifier's real subprocess-based instantiation (isolated hive, correct
/// argument handling) — confirmed empirically: an include pattern matching zero files causes
/// <see cref="VerificationEngine.Execute"/> to complete without throwing and without requiring any
/// checked-in <c>*.verified.*</c> snapshot.
/// </summary>
internal static class TemporalWorkerTestHelper
{
    /// <summary>
    /// Instantiates <c>temporal-worker</c> into <paramref name="outputDirectory"/> (must already
    /// exist and be empty) with the given <c>Framework</c>/<c>IncludeOtel</c> symbols.
    /// </summary>
    public static async Task InstantiateAsync(
        string name,
        string framework,
        bool includeOtel,
        string outputDirectory,
        string settingsDirectory)
    {
        var options = new TemplateVerifierOptions(templateName: "temporal-worker")
        {
            TemplatePath = RepoPaths.ContentRoot("TemporalWorker"),
            OutputDirectory = outputDirectory,
            SettingsDirectory = settingsDirectory,
            TemplateSpecificArgs = new[]
            {
                "-n", name,
                "-o", ".",
                "--framework", framework,
                "--include-otel", includeOtel ? "true" : "false",
            },
            // Matches no file — see class remarks. This template's tests assert on content and
            // build success directly rather than via checked-in snapshots.
            VerificationIncludePatterns = new[] { "__no-snapshot-verification__" },
        };

        var engine = new VerificationEngine(NullLoggerFactory.Instance);
        await engine.Execute(Options.Create(options)).ConfigureAwait(false);
    }
}
