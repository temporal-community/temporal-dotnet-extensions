using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.TemplateEngine.Authoring.TemplateVerifier;
using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Covers both naming paths for the <c>temporal-workflow</c> item template, using two different
/// mechanisms — TemplateVerifier auto-appends <c>-n &lt;shortName&gt;</c> when no name is
/// supplied, and a shortName containing hyphens is not a valid C# identifier, so the "no name"
/// path can't be exercised through TemplateVerifier itself. See docs/templates.md and the plan
/// this implements for the full rationale.
/// Also covers the custom-name path for the <c>temporal-activity</c>
/// and <c>temporal-converter</c> item templates, following the same
/// instantiate/snapshot/rebuild pattern (the default-name-via-custom-hive path is already
/// exercised once above and
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
        var nestedFixtureCopy = TestFixtures.CopyHostProjectToTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        var nestedSettingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            // Same restore requirement as the temporal-workflow case above — the "csharp-only"
            // constraint needs a restored project to evaluate.
            await DotnetCli.RunAsync(fixtureCopy, "restore");
            await DotnetCli.RunAsync(nestedFixtureCopy, "restore");

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

            var nestedOptions = new TemplateVerifierOptions(templateName: "temporal-activity")
            {
                TemplatePath = RepoPaths.ContentRoot("TemporalActivity"),
                OutputDirectory = nestedFixtureCopy,
                SettingsDirectory = nestedSettingsDirectory,
                EnsureEmptyOutputDirectory = false,
                TemplateSpecificArgs = new[] { "-n", "NestedActivityName", "-o", "." },
                VerificationIncludePatterns = new[] { "NestedActivityName.cs" },
            };
            await new VerificationEngine(NullLoggerFactory.Instance)
                .Execute(Options.Create(nestedOptions));

            var nestedContent = await File.ReadAllTextAsync(
                Path.Combine(nestedFixtureCopy, "NestedActivityName.cs"));
            Assert.NotEqual(
                ExtractActivityMethodName(generatedContent),
                ExtractActivityMethodName(nestedContent));

            await DotnetCli.RunAsync(fixtureCopy, "build", "--nologo");

            var harnessDirectory = TestFixtures.CreateTempDirectory();
            try
            {
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
                          <PackageReference Include="Temporalio" Version="1.16.0" />
                          <Reference Include="HostProject">
                            <HintPath>{Path.Combine(fixtureCopy, "bin", "Debug", "net10.0", "HostProject.dll")}</HintPath>
                          </Reference>
                        </ItemGroup>
                    </Project>
                    """);
                await File.WriteAllTextAsync(
                    Path.Combine(harnessDirectory, "Program.cs"),
                    """
                    using Temporalio.Activities;
                    using Fixtures.HostProject;

                    var first = ActivityDefinition.CreateAll(typeof(CustomActivityName), new CustomActivityName()).Single();
                    Console.WriteLine(first.Name);
                    """);
                await DotnetCli.RunAsync(harnessDirectory, "restore", "--nologo");
                var output = await DotnetCli.RunAsync(harnessDirectory, "run", "--project", "Harness.csproj");
                Assert.Contains("CustomActivityName", output, StringComparison.Ordinal);
            }
            finally
            {
                TestFixtures.DeleteDirectory(harnessDirectory);
            }
        }
        finally
        {
            TestFixtures.DeleteDirectory(fixtureCopy);
            TestFixtures.DeleteDirectory(settingsDirectory);
        }
    }

    [Theory]
    [InlineData("Foo-Bar", "Foo_Bar", "Foo_BarAsync")]
    [InlineData("9Lives", "_9Lives", "_9LivesAsync")]
    public async Task TemporalActivityMethodNameUsesGeneratedClassIdentifier(
        string name,
        string generatedClassName,
        string generatedMethodName)
    {
        var fixtureCopy = TestFixtures.CopyHostProjectToTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            await DotnetCli.RunAsync(fixtureCopy, "restore");

            var options = new TemplateVerifierOptions(templateName: "temporal-activity")
            {
                TemplatePath = RepoPaths.ContentRoot("TemporalActivity"),
                OutputDirectory = fixtureCopy,
                SettingsDirectory = settingsDirectory,
                EnsureEmptyOutputDirectory = false,
                TemplateSpecificArgs = new[] { "-n", name, "-o", "." },
                VerificationIncludePatterns = new[] { "__no-snapshot-verification__" },
            };

            await new VerificationEngine(NullLoggerFactory.Instance)
                .Execute(Options.Create(options));

            var generatedPath = Directory.GetFiles(fixtureCopy, "*.cs").Single();
            var generatedContent = await File.ReadAllTextAsync(generatedPath);
            Assert.Contains($"class {generatedClassName}", generatedContent, StringComparison.Ordinal);
            Assert.Contains(
                $"public async Task<string> {generatedMethodName}(",
                generatedContent,
                StringComparison.Ordinal);

            await DotnetCli.RunAsync(fixtureCopy, "build", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(fixtureCopy);
            TestFixtures.DeleteDirectory(settingsDirectory);
        }
    }

    [Fact]
    public async Task TemporalConverterWithCustomNameGeneratesFileAndBuilds()
    {
        var fixtureCopy = TestFixtures.CopyHostProjectToTempDirectory();
        var secondFixtureCopy = TestFixtures.CopyHostProjectToTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        var secondSettingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            await DotnetCli.RunAsync(fixtureCopy, "restore");
            await DotnetCli.RunAsync(secondFixtureCopy, "restore");

            var options = new TemplateVerifierOptions(templateName: "temporal-converter")
            {
                TemplatePath = RepoPaths.ContentRoot("TemporalConverter"),
                OutputDirectory = fixtureCopy,
                SettingsDirectory = settingsDirectory,
                EnsureEmptyOutputDirectory = false,
                TemplateSpecificArgs = new[] { "-n", "CustomConverterName", "-o", "." },
                VerificationIncludePatterns = new[] { "CustomConverterName.cs" },
            };

            await new VerificationEngine(NullLoggerFactory.Instance).Execute(Options.Create(options));

            var generatedContent = await File.ReadAllTextAsync(
                Path.Combine(fixtureCopy, "CustomConverterName.cs"));
            Assert.Contains("class CustomConverterName : global::Temporalio.Converters.DefaultPayloadConverter", generatedContent, StringComparison.Ordinal);
            Assert.Contains("class CustomConverterNameEncoding : global::Temporalio.Converters.IEncodingConverter", generatedContent, StringComparison.Ordinal);
            Assert.Contains("namespace Fixtures.HostProject;", generatedContent, StringComparison.Ordinal);
            Assert.Contains("custom/Fixtures.HostProject.CustomConverterName/v1", generatedContent, StringComparison.Ordinal);
            // The custom encoding must be tried before JsonPlainConverter, which accepts any value.
            Assert.True(
                generatedContent.IndexOf("new CustomConverterNameEncoding()", StringComparison.Ordinal)
                    < generatedContent.IndexOf("new global::Temporalio.Converters.JsonPlainConverter(", StringComparison.Ordinal),
                "Custom encoding converter must precede JsonPlainConverter.");

            var secondOptions = new TemplateVerifierOptions(templateName: "temporal-converter")
            {
                TemplatePath = RepoPaths.ContentRoot("TemporalConverter"),
                OutputDirectory = secondFixtureCopy,
                SettingsDirectory = secondSettingsDirectory,
                EnsureEmptyOutputDirectory = false,
                TemplateSpecificArgs = new[] { "-n", "SecondConverter", "-o", "." },
                VerificationIncludePatterns = new[] { "SecondConverter.cs" },
            };
            await new VerificationEngine(NullLoggerFactory.Instance).Execute(Options.Create(secondOptions));

            var secondContent = await File.ReadAllTextAsync(Path.Combine(secondFixtureCopy, "SecondConverter.cs"));
            Assert.Contains("custom/Fixtures.HostProject.SecondConverter/v1", secondContent, StringComparison.Ordinal);
            Assert.NotEqual(ExtractEncoding(generatedContent), ExtractEncoding(secondContent));

            await DotnetCli.RunAsync(fixtureCopy, "build", "--nologo");
            await DotnetCli.RunAsync(secondFixtureCopy, "build", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(fixtureCopy);
            TestFixtures.DeleteDirectory(secondFixtureCopy);
            TestFixtures.DeleteDirectory(settingsDirectory);
            TestFixtures.DeleteDirectory(secondSettingsDirectory);
        }
    }

    private static string ExtractActivityMethodName(string content)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            @"\[Activity\][\s\S]*?public\s+(?:async\s+)?(?:Task<string>|string)\s+(?<name>\w+)\(");
        Assert.True(match.Success, "Generated activity method was not found.");
        return match.Groups["name"].Value;
    }

    private static string ExtractEncoding(string content)
    {
        const string marker = "Encoding => ";
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Generated encoding was not found.");
        start += marker.Length;
        var end = content.IndexOf(';', start);
        return content[start..end];
    }
}
