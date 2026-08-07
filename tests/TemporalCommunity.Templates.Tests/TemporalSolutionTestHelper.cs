using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.TemplateEngine.Authoring.TemplateVerifier;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Shared instantiation helper for the <c>temporal-solution</c> multi-project template. Follows the
/// same pattern as <see cref="TemporalWorkerTestHelper"/>: asserts directly on generated file
/// content and <c>dotnet build</c> success rather than checked-in snapshots, so
/// <see cref="TemplateVerifierOptions.VerificationIncludePatterns"/> is set to a pattern that
/// matches no file.
/// </summary>
internal static class TemporalSolutionTestHelper
{
    public static async Task InstantiateAsync(
        string name,
        string framework,
        bool includeAspire,
        bool includeOtel,
        string outputDirectory,
        string settingsDirectory)
    {
        var options = new TemplateVerifierOptions(templateName: "temporal-solution")
        {
            TemplatePath = RepoPaths.ContentRoot("TemporalSolution"),
            OutputDirectory = outputDirectory,
            SettingsDirectory = settingsDirectory,
            TemplateSpecificArgs = new[]
            {
                "-n", name,
                "-o", ".",
                "--framework", framework,
                "--include-aspire", includeAspire ? "true" : "false",
                "--include-otel", includeOtel ? "true" : "false",
            },
            VerificationIncludePatterns = new[] { "__no-snapshot-verification__" },
        };

        var engine = new VerificationEngine(NullLoggerFactory.Instance);
        await engine.Execute(Options.Create(options)).ConfigureAwait(false);
    }
}
