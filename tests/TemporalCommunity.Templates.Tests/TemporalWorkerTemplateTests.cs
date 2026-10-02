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
    [Fact]
    public async Task HelperNameAsProjectNameGeneratesAndBuilds()
    {
        const string name = "TemporalWorkerConnection";
        var outputDirectory = TestFixtures.CreateTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            await TemporalWorkerTestHelper.InstantiateAsync(
                name, "net10.0", includeOtel: false, outputDirectory, settingsDirectory);

            var programContent = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "Program.cs"));
            Assert.Contains(
                $"global::{name}.TemporalWorkerConnection.Resolve(builder.Configuration);",
                programContent, StringComparison.Ordinal);
            Assert.Contains(
                $"global::{name}.TemporalWorkerConnection.ApplyTo(connectOptions, options);",
                programContent, StringComparison.Ordinal);
            await DotnetCli.RunAsync(outputDirectory, "build", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(outputDirectory);
            TestFixtures.DeleteDirectory(settingsDirectory);
        }
    }

    [Fact]
    public async Task GenerationRunsRestoreBeforeNoRestoreBuild()
    {
        var rootDirectory = TestFixtures.CreateTempDirectory();
        var outputDirectory = Path.Combine(rootDirectory, "WorkerRestoreProbe");
        var hiveDirectory = Path.Combine(rootDirectory, "hive");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            await DotnetCli.RunAsync(
                rootDirectory,
                "new", "install", RepoPaths.ContentRoot("TemporalWorker"),
                "--debug:custom-hive", hiveDirectory);
            await DotnetCli.RunAsync(
                rootDirectory,
                "new", "temporal-worker",
                "-n", "WorkerRestoreProbe",
                "-o", outputDirectory,
                "--framework", "net10.0",
                "--debug:custom-hive", hiveDirectory);

            var assetsPath = Path.Combine(outputDirectory, "obj", "project.assets.json");
            Assert.True(
                File.Exists(assetsPath),
                $"Template restore post-action did not create '{assetsPath}' before build.");
            await DotnetCli.RunAsync(outputDirectory, "build", "--no-restore", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task OmittingNameUsesOutputDirectoryName()
    {
        var rootDirectory = TestFixtures.CreateTempDirectory();
        var outputDirectory = Path.Combine(rootDirectory, "WorkerDefault");
        var hiveDirectory = Path.Combine(rootDirectory, "hive");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            await DotnetCli.RunAsync(
                rootDirectory,
                "new", "install", RepoPaths.ContentRoot("TemporalWorker"),
                "--debug:custom-hive", hiveDirectory);
            await DotnetCli.RunAsync(
                outputDirectory,
                "new", "temporal-worker",
                "-o", ".",
                "--debug:custom-hive", hiveDirectory);

            var projectPath = Path.Combine(outputDirectory, "WorkerDefault.csproj");
            var programPath = Path.Combine(outputDirectory, "Program.cs");
            Assert.True(File.Exists(projectPath), $"Expected generated project at '{projectPath}'.");
            Assert.True(File.Exists(programPath), $"Expected generated program at '{programPath}'.");

            var programContent = await File.ReadAllTextAsync(programPath);
            Assert.Equal("WorkerDefault-tq", ExtractQueueDefault(programContent));
            await DotnetCli.RunAsync(outputDirectory, "build", "--no-restore", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(rootDirectory);
        }
    }

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
            Assert.Contains("Temporal:TaskQueue", programContent, StringComparison.Ordinal);
            Assert.Contains("IsNullOrWhiteSpace", programContent, StringComparison.Ordinal);
            Assert.Equal($"{name}-tq", ExtractQueueDefault(programContent));
            Assert.Contains(
                $"global::{name}.TemporalWorkerConnection.Resolve(builder.Configuration);",
                programContent, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(outputDirectory, "TemporalWorkerConnection.cs")));
            Assert.False(File.Exists(Path.Combine(outputDirectory, "TemporalConnection.cs")));

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

            // ITemporalClient is registered through the SDK's AddTemporalClient, with ApplyTo
            // transferring the resolved settings (its behavior is covered by
            // TemporalConnectionResolverTests), rather than a hand-rolled lazy-client singleton.
            Assert.Contains("builder.Services.AddTemporalClient(options =>", programContent, StringComparison.Ordinal);
            Assert.Contains(
                $"global::{name}.TemporalWorkerConnection.ApplyTo(connectOptions, options);",
                programContent, StringComparison.Ordinal);
            Assert.DoesNotContain("AddSingleton<ITemporalClient>", programContent, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateLazy", programContent, StringComparison.Ordinal);
            Assert.DoesNotContain("options.LoggerFactory", programContent, StringComparison.Ordinal);

            // TracingInterceptor is only ever composed once (appended to the client options'
            // existing interceptors inside the AddTemporalClient callback) — never a second time on
            // TemporalWorkerOptions, which would double every span since the client interceptor
            // already carries over into the worker automatically.
            AssertSingleTracingInterceptorComposition(programContent, includeOtel);

            // Standalone build: the generated project must compile on its own, not just as part of
            // the repo's own solution.
            await DotnetCli.RunAsync(outputDirectory, "build", "--nologo");
            if (framework == "net10.0" && !includeOtel)
            {
                await TaskQueueResolverHarness.AssertContractAsync(
                    programPath,
                    $"{name}-tq",
                    "ResolveWorkerTaskQueue");
            }
        }
        finally
        {
            TestFixtures.DeleteDirectory(outputDirectory);
            TestFixtures.DeleteDirectory(settingsDirectory);
        }
    }

    internal static void AssertSingleTracingInterceptorComposition(string programContent, bool includeOtel)
    {
        var expected = includeOtel ? 1 : 0;
        Assert.Equal(expected, System.Text.RegularExpressions.Regex.Count(
            programContent,
            System.Text.RegularExpressions.Regex.Escape(TemporalConnectionResolverHarnessProgram.OtelInterceptorComposition)));
        Assert.Equal(expected, System.Text.RegularExpressions.Regex.Count(programContent, "new TracingInterceptor\\(\\)"));
    }

    private static string ExtractQueueDefault(string content)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            "const string taskQueue\\s*=\\s*\"(?<queue>[^\"]+)\"");
        Assert.True(match.Success, "Generated queue default was not found.");
        return match.Groups["queue"].Value;
    }
}
