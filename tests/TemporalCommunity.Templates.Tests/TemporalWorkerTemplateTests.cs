using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Covers the full <c>Framework</c> x <c>IncludeOtel</c> 2x2 matrix for the <c>temporal-worker</c>
/// project template: asserts the generated <c>Program.cs</c>/<c>.csproj</c> contain (or omit) the
/// OpenTelemetry wiring correctly, and that the generated project builds standalone for both
/// target frameworks.
/// </summary>
public sealed class TemporalWorkerTemplateTests
{
    [Theory]
    [InlineData("net8.0", false)]
    [InlineData("net8.0", true)]
    [InlineData("net10.0", false)]
    [InlineData("net10.0", true)]
    public async Task GeneratesExpectedContentAndBuilds(string framework, bool includeOtel)
    {
        ArgumentNullException.ThrowIfNull(framework);

        var outputDirectory = TestFixtures.CreateTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            var name = $"Worker{framework.Replace(".", string.Empty, StringComparison.Ordinal)}{(includeOtel ? "Otel" : "Plain")}";

            await TemporalWorkerTestHelper.InstantiateAsync(
                name, framework, includeOtel, outputDirectory, settingsDirectory);

            var csprojPath = Path.Combine(outputDirectory, $"{name}.csproj");
            var programPath = Path.Combine(outputDirectory, "Program.cs");
            Assert.True(File.Exists(csprojPath), $"Expected generated csproj at '{csprojPath}'.");
            Assert.True(File.Exists(programPath), $"Expected generated Program.cs at '{programPath}'.");

            var csprojContent = await File.ReadAllTextAsync(csprojPath);
            var programContent = await File.ReadAllTextAsync(programPath);

            Assert.Contains($"<TargetFramework>{framework}</TargetFramework>", csprojContent, StringComparison.Ordinal);

            if (includeOtel)
            {
                Assert.Contains("AddSource(\"Temporalio\")", programContent, StringComparison.Ordinal);
                Assert.Contains("UseOtlpExporter()", programContent, StringComparison.Ordinal);
                Assert.Contains("new TracingInterceptor()", programContent, StringComparison.Ordinal);
                Assert.Contains("Temporalio.Extensions.OpenTelemetry", csprojContent, StringComparison.Ordinal);
                Assert.Contains("OpenTelemetry.Extensions.Hosting", csprojContent, StringComparison.Ordinal);
                Assert.Contains("OpenTelemetry.Exporter.OpenTelemetryProtocol", csprojContent, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("AddSource(\"Temporalio\")", programContent, StringComparison.Ordinal);
                Assert.DoesNotContain("UseOtlpExporter()", programContent, StringComparison.Ordinal);
                Assert.DoesNotContain("TracingInterceptor", programContent, StringComparison.Ordinal);
                Assert.DoesNotContain("Temporalio.Extensions.OpenTelemetry", csprojContent, StringComparison.Ordinal);
                Assert.DoesNotContain("OpenTelemetry.Extensions.Hosting", csprojContent, StringComparison.Ordinal);
                Assert.DoesNotContain("OpenTelemetry.Exporter.OpenTelemetryProtocol", csprojContent, StringComparison.Ordinal);
            }

            // TracingInterceptor is only ever assigned once (on the cloned client options, inside
            // the ITemporalClient factory) — never a second time on TemporalWorkerOptions, which
            // would double every span since the client interceptor already carries over into the
            // worker automatically. Counted rather than checked textually, since IncludeOtel's own
            // explanatory comment legitimately mentions "TemporalWorkerOptions.Interceptors" by name.
            var interceptorAssignmentCount = System.Text.RegularExpressions.Regex.Count(
                programContent, "\\.Interceptors = new\\[\\] \\{ new TracingInterceptor\\(\\) \\};");
            Assert.Equal(includeOtel ? 1 : 0, interceptorAssignmentCount);

            // Standalone build: the generated project must compile on its own, not just as part of
            // the repo's own solution.
            await DotnetCli.RunAsync(outputDirectory, "build", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(outputDirectory);
            TestFixtures.DeleteDirectory(settingsDirectory);
        }
    }
}
