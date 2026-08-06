using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.TemplateEngine.Authoring.TemplateVerifier;
using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Covers both naming paths for the <c>temporal-workflow</c> item template, using two different
/// mechanisms — TemplateVerifier auto-appends <c>-n &lt;shortName&gt;</c> when no name is
/// supplied, and a shortName containing hyphens is not a valid C# identifier, so the "no name"
/// path can't be exercised through TemplateVerifier itself. See docs/TEMPLATES.md and the plan
/// this implements for the full rationale.
/// Also covers the custom-name path for the <c>temporal-activity</c> and
/// <c>temporal-payload-converter</c> item templates, following the same instantiate/snapshot/
/// rebuild pattern (the default-name-via-custom-hive path is already exercised once above and
/// isn't re-verified per template).
/// </summary>
public sealed class TemplateVerifierTests
{
    [Fact]
    public async Task TemporalWorkflowWithCustomNameGeneratesFileAndBuilds()
    {
        var fixtureCopy = TestFixtures.CopyHostProjectToTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            // The "csharp-only" project-capability constraint on temporal-workflow can only be
            // evaluated against a restored project — confirmed empirically: an unrestored fixture
            // copy fails instantiation with exit code 100 ("Project capabilities: ... is not
            // restored"), even though the fixture is a valid C# project.
            await DotnetCli.RunAsync(fixtureCopy, "restore");

            var options = new TemplateVerifierOptions(templateName: "temporal-workflow")
            {
                TemplatePath = RepoPaths.ContentRoot("TemporalWorkflow"),
                OutputDirectory = fixtureCopy,
                SettingsDirectory = settingsDirectory,
                // The fixture copy is a non-empty, pre-populated project directory, not a fresh
                // empty folder — EnsureEmptyOutputDirectory defaults to true and would fail here.
                EnsureEmptyOutputDirectory = false,
                // Explicit -n and -o: TemplateVerifier's own auto-append of "-n <shortName> -o
                // <shortName>" only fires when neither flag is already present.
                TemplateSpecificArgs = new[] { "-n", "CustomWorkflowName", "-o", "." },
                // Restrict the snapshot diff to the one freshly generated file; the fixture's own
                // pre-existing project files are not part of what this test verifies.
                VerificationIncludePatterns = new[] { "CustomWorkflowName.cs" },
            };

            var engine = new VerificationEngine(NullLoggerFactory.Instance);
            await engine.Execute(Options.Create(options));

            var generatedFilePath = Path.Combine(fixtureCopy, "CustomWorkflowName.cs");
            Assert.True(File.Exists(generatedFilePath), $"Expected generated file at '{generatedFilePath}'.");

            var generatedContent = await File.ReadAllTextAsync(generatedFilePath);
            Assert.Contains("class CustomWorkflowName", generatedContent, StringComparison.Ordinal);
            Assert.Contains("namespace Fixtures.HostProject;", generatedContent, StringComparison.Ordinal);

            await DotnetCli.RunAsync(fixtureCopy, "build", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(fixtureCopy);
            TestFixtures.DeleteDirectory(settingsDirectory);
        }
    }

    [Fact]
    public async Task TemporalWorkflowWithDefaultNameViaCustomHiveGeneratesPlaceholderFileAndBuilds()
    {
        var fixtureCopy = TestFixtures.CopyHostProjectToTempDirectory();
        // Fully self-contained: this test installs into and instantiates from its own temp hive,
        // never one shared with another test — xUnit gives no ordering/parallelism guarantee
        // between test methods, so sharing a hive would be a race.
        var hiveDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            var templatePath = RepoPaths.ContentRoot("TemporalWorkflow");

            // Same restore requirement as the custom-name case above — the "csharp-only"
            // constraint needs a restored project to evaluate.
            await DotnetCli.RunAsync(fixtureCopy, "restore");

            // "dotnet new install" does not support "--nologo" (confirmed empirically: it fails
            // with "'--nologo' is not supported").
            await DotnetCli.RunAsync(
                fixtureCopy,
                "new", "install", templatePath, "--debug:custom-hive", hiveDirectory);

            // No -n: exercises the true defaultName/preferDefaultName fallback. A bare `dotnet
            // new` here would fall back to the real user-wide hive and report "template not
            // found," since the packed template was only ever installed into this custom hive.
            await DotnetCli.RunAsync(
                fixtureCopy,
                "new", "temporal-workflow", "-o", ".", "--debug:custom-hive", hiveDirectory);

            var generatedFilePath = Path.Combine(fixtureCopy, "TemporalWorkflow1.cs");
            Assert.True(File.Exists(generatedFilePath), $"Expected generated file at '{generatedFilePath}'.");

            var generatedContent = await File.ReadAllTextAsync(generatedFilePath);
            Assert.Contains("class TemporalWorkflow1", generatedContent, StringComparison.Ordinal);
            Assert.Contains("namespace Fixtures.HostProject;", generatedContent, StringComparison.Ordinal);

            await DotnetCli.RunAsync(fixtureCopy, "build", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(fixtureCopy);
            TestFixtures.DeleteDirectory(hiveDirectory);
        }
    }

    [Fact]
    public async Task TemporalActivityWithCustomNameGeneratesFileAndBuilds()
    {
        var fixtureCopy = TestFixtures.CopyHostProjectToTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            // Same restore requirement as the temporal-workflow case above — the "csharp-only"
            // constraint needs a restored project to evaluate.
            await DotnetCli.RunAsync(fixtureCopy, "restore");

            var options = new TemplateVerifierOptions(templateName: "temporal-activity")
            {
                TemplatePath = RepoPaths.ContentRoot("TemporalActivity"),
                OutputDirectory = fixtureCopy,
                SettingsDirectory = settingsDirectory,
                EnsureEmptyOutputDirectory = false,
                TemplateSpecificArgs = new[] { "-n", "CustomActivityName", "-o", "." },
                VerificationIncludePatterns = new[] { "CustomActivityName.cs" },
            };

            var engine = new VerificationEngine(NullLoggerFactory.Instance);
            await engine.Execute(Options.Create(options));

            var generatedFilePath = Path.Combine(fixtureCopy, "CustomActivityName.cs");
            Assert.True(File.Exists(generatedFilePath), $"Expected generated file at '{generatedFilePath}'.");

            var generatedContent = await File.ReadAllTextAsync(generatedFilePath);
            Assert.Contains("class CustomActivityName", generatedContent, StringComparison.Ordinal);
            Assert.Contains("namespace Fixtures.HostProject;", generatedContent, StringComparison.Ordinal);
            Assert.Contains("[Activity]", generatedContent, StringComparison.Ordinal);

            await DotnetCli.RunAsync(fixtureCopy, "build", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(fixtureCopy);
            TestFixtures.DeleteDirectory(settingsDirectory);
        }
    }

    [Fact]
    public async Task TemporalPayloadConverterWithCustomNameGeneratesFileAndBuilds()
    {
        var fixtureCopy = TestFixtures.CopyHostProjectToTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            // Same restore requirement as the temporal-workflow case above — the "csharp-only"
            // constraint needs a restored project to evaluate.
            await DotnetCli.RunAsync(fixtureCopy, "restore");

            var options = new TemplateVerifierOptions(templateName: "temporal-payload-converter")
            {
                TemplatePath = RepoPaths.ContentRoot("TemporalPayloadConverter"),
                OutputDirectory = fixtureCopy,
                SettingsDirectory = settingsDirectory,
                EnsureEmptyOutputDirectory = false,
                TemplateSpecificArgs = new[] { "-n", "CustomPayloadConverterName", "-o", "." },
                VerificationIncludePatterns = new[] { "CustomPayloadConverterName.cs" },
            };

            var engine = new VerificationEngine(NullLoggerFactory.Instance);
            await engine.Execute(Options.Create(options));

            var generatedFilePath = Path.Combine(fixtureCopy, "CustomPayloadConverterName.cs");
            Assert.True(File.Exists(generatedFilePath), $"Expected generated file at '{generatedFilePath}'.");

            var generatedContent = await File.ReadAllTextAsync(generatedFilePath);
            Assert.Contains("class CustomPayloadConverterName", generatedContent, StringComparison.Ordinal);
            Assert.Contains("namespace Fixtures.HostProject;", generatedContent, StringComparison.Ordinal);
            Assert.Contains(": IEncodingConverter", generatedContent, StringComparison.Ordinal);

            await DotnetCli.RunAsync(fixtureCopy, "build", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(fixtureCopy);
            TestFixtures.DeleteDirectory(settingsDirectory);
        }
    }
}
