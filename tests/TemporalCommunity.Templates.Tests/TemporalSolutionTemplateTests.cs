using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Covers the full <c>Framework</c> x <c>IncludeAspire</c> x <c>IncludeOtel</c> matrix for the
/// <c>temporal-solution</c> multi-project template: asserts AppHost/ServiceDefaults presence,
/// the two OTel-related conditions tracked separately (<c>IncludeOtel</c> for the
/// AddSource/TracingInterceptor registration, <c>OtelWithoutAspire</c> for the standalone OTLP
/// exporter), and that the generated solution builds end-to-end via its <c>.sln</c>.
/// </summary>
public sealed class TemporalSolutionTemplateTests
{
    public static IEnumerable<object[]> Combinations()
    {
        foreach (var framework in new[] { "net8.0", "net10.0" })
        {
            foreach (var includeAspire in new[] { false, true })
            {
                foreach (var includeOtel in new[] { false, true })
                {
                    yield return new object[] { framework, includeAspire, includeOtel };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task GeneratesExpectedShapeAndBuilds(string framework, bool includeAspire, bool includeOtel)
    {
        ArgumentNullException.ThrowIfNull(framework);

        var outputDirectory = TestFixtures.CreateTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            var name = $"Sol{framework.Replace(".", string.Empty, StringComparison.Ordinal)}" +
                $"{(includeAspire ? "Aspire" : "NoAspire")}{(includeOtel ? "Otel" : "NoOtel")}";

            await TemporalSolutionTestHelper.InstantiateAsync(
                name, framework, includeAspire, includeOtel, outputDirectory, settingsDirectory);

            var slnPath = Path.Combine(outputDirectory, $"{name}.sln");
            var workerCsprojPath = Path.Combine(outputDirectory, $"{name}.Worker", $"{name}.Worker.csproj");
            var clientCsprojPath = Path.Combine(outputDirectory, $"{name}.Client", $"{name}.Client.csproj");
            var sharedCsprojPath = Path.Combine(outputDirectory, $"{name}.Shared", $"{name}.Shared.csproj");
            var workerProgramPath = Path.Combine(outputDirectory, $"{name}.Worker", "Program.cs");
            var appHostDirectory = Path.Combine(outputDirectory, $"{name}.AppHost");
            var serviceDefaultsDirectory = Path.Combine(outputDirectory, $"{name}.ServiceDefaults");

            Assert.True(File.Exists(slnPath), $"Expected solution file at '{slnPath}'.");
            Assert.True(File.Exists(workerCsprojPath), $"Expected Worker csproj at '{workerCsprojPath}'.");
            Assert.True(File.Exists(clientCsprojPath), $"Expected Client csproj at '{clientCsprojPath}'.");
            Assert.True(File.Exists(sharedCsprojPath), $"Expected Shared csproj at '{sharedCsprojPath}'.");

            var slnContent = await File.ReadAllTextAsync(slnPath);
            var workerProgramContent = await File.ReadAllTextAsync(workerProgramPath);

            if (includeAspire)
            {
                Assert.True(Directory.Exists(appHostDirectory), $"Expected AppHost directory at '{appHostDirectory}'.");
                Assert.True(Directory.Exists(serviceDefaultsDirectory), $"Expected ServiceDefaults directory at '{serviceDefaultsDirectory}'.");
                Assert.Contains($"{name}.AppHost", slnContent, StringComparison.Ordinal);
                Assert.Contains($"{name}.ServiceDefaults", slnContent, StringComparison.Ordinal);
                Assert.Contains("AddServiceDefaults()", workerProgramContent, StringComparison.Ordinal);
            }
            else
            {
                Assert.False(Directory.Exists(appHostDirectory), $"Did not expect AppHost directory at '{appHostDirectory}'.");
                Assert.False(Directory.Exists(serviceDefaultsDirectory), $"Did not expect ServiceDefaults directory at '{serviceDefaultsDirectory}'.");
                Assert.DoesNotContain($"{name}.AppHost", slnContent, StringComparison.Ordinal);
                Assert.DoesNotContain($"{name}.ServiceDefaults", slnContent, StringComparison.Ordinal);
                Assert.DoesNotContain("AddServiceDefaults()", workerProgramContent, StringComparison.Ordinal);
            }

            // IncludeOtel tracks AddSource/TracingInterceptor registration, independent of IncludeAspire.
            if (includeOtel)
            {
                Assert.Contains("AddSource(\"Temporalio\")", workerProgramContent, StringComparison.Ordinal);
                Assert.Contains("new TracingInterceptor()", workerProgramContent, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("AddSource(\"Temporalio\")", workerProgramContent, StringComparison.Ordinal);
                Assert.DoesNotContain("TracingInterceptor", workerProgramContent, StringComparison.Ordinal);
            }

            var interceptorAssignmentCount = System.Text.RegularExpressions.Regex.Count(
                workerProgramContent, "\\.Interceptors = new\\[\\] \\{ new TracingInterceptor\\(\\) \\};");
            Assert.Equal(includeOtel ? 1 : 0, interceptorAssignmentCount);

            // OtelWithoutAspire tracks the standalone OTLP exporter — present only when
            // IncludeOtel=true and IncludeAspire=false; ServiceDefaults owns the exporter otherwise.
            var otelWithoutAspire = includeOtel && !includeAspire;
            if (otelWithoutAspire)
            {
                Assert.Contains("UseOtlpExporter()", workerProgramContent, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("UseOtlpExporter()", workerProgramContent, StringComparison.Ordinal);
            }

            // Standalone build via the generated .sln — not just individual projects — to catch
            // broken ProjectReference paths from the shared sourceName token substitution.
            await DotnetCli.RunAsync(outputDirectory, "build", slnPath, "--nologo", "-m:1");
        }
        finally
        {
            TestFixtures.DeleteDirectory(outputDirectory);
            TestFixtures.DeleteDirectory(settingsDirectory);
        }
    }

    [Fact]
    public async Task XmlSensitiveNameSanitizesIdentifiersAndXmlEncodesProjectReferences()
    {
        var outputDirectory = TestFixtures.CreateTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            const string name = "Contoso-Fulfillment&Orders";
            await TemporalSolutionTestHelper.InstantiateAsync(
                name, "net10.0", includeAspire: true, includeOtel: false, outputDirectory, settingsDirectory);

            var appHostCsPath = Path.Combine(outputDirectory, $"{name}.AppHost", "AppHost.cs");
            Assert.True(File.Exists(appHostCsPath), $"Expected AppHost.cs at '{appHostCsPath}'.");
            var appHostContent = await File.ReadAllTextAsync(appHostCsPath);

            // GeneratedClassNamePrefix sanitizes the hyphen and the '&' into '_' for the strongly
            // typed Projects.* references — a raw "Contoso-Fulfillment&Orders_Worker" would not be
            // a valid C# identifier.
            Assert.Contains("Projects.Contoso_Fulfillment_Orders_Worker", appHostContent, StringComparison.Ordinal);
            Assert.Contains("Projects.Contoso_Fulfillment_Orders_Client", appHostContent, StringComparison.Ordinal);

            var workerCsprojPath = Path.Combine(outputDirectory, $"{name}.Worker", $"{name}.Worker.csproj");
            Assert.True(File.Exists(workerCsprojPath), $"Expected Worker csproj at '{workerCsprojPath}'.");
            var workerCsprojContent = await File.ReadAllTextAsync(workerCsprojPath);

            // XmlEncodedProjectName XML-escapes the '&' (as "&amp;") for use inside XML attribute
            // text — the folder on disk keeps the raw, unescaped '&' (filesystems allow it), so the
            // reference must be escaped to remain well-formed XML.
            Assert.Contains("Contoso-Fulfillment&amp;Orders.Shared", workerCsprojContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Contoso-Fulfillment&Orders.Shared\"", workerCsprojContent, StringComparison.Ordinal);

            // The .csproj must still parse as well-formed XML — a raw, unescaped '&' would not.
            _ = System.Xml.Linq.XDocument.Parse(workerCsprojContent);

            var slnPath = Path.Combine(outputDirectory, $"{name}.sln");
            await DotnetCli.RunAsync(outputDirectory, "build", slnPath, "--nologo", "-m:1");
        }
        finally
        {
            TestFixtures.DeleteDirectory(outputDirectory);
            TestFixtures.DeleteDirectory(settingsDirectory);
        }
    }
}
