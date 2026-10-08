using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Covers the full <c>Framework</c> x <c>IncludeOtel</c> x <c>UseMinimalApi</c> matrix for the <c>temporal-worker</c>
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
                $"{name}.TemporalWorkerConnection.Resolve(builder.Configuration);",
                programContent, StringComparison.Ordinal);
            Assert.Contains(
                $"{name}.TemporalWorkerConnection.ApplyTo(connectOptions, options);",
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
    [InlineData("net8.0", false, false)]
    [InlineData("net8.0", false, true)]
    [InlineData("net8.0", true, false)]
    [InlineData("net8.0", true, true)]
    [InlineData("net10.0", false, false)]
    [InlineData("net10.0", false, true)]
    [InlineData("net10.0", true, false)]
    [InlineData("net10.0", true, true)]
    public async Task GeneratesExpectedContentAndBuilds(string framework, bool includeOtel, bool useMinimalApi)
    {
        ArgumentNullException.ThrowIfNull(framework);

        var outputDirectory = TestFixtures.CreateTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            var name = $"Worker{framework.Replace(".", string.Empty, StringComparison.Ordinal)}{(includeOtel ? "Otel" : "Plain")}{(useMinimalApi ? "MinimalApi" : "Console")}";

            await TemporalWorkerTestHelper.InstantiateAsync(
                name, framework, includeOtel, outputDirectory, settingsDirectory, useMinimalApi);

            var csprojPath = Path.Combine(outputDirectory, $"{name}.csproj");
            var programPath = Path.Combine(outputDirectory, "Program.cs");
            Assert.True(File.Exists(csprojPath), $"Expected generated csproj at '{csprojPath}'.");
            Assert.True(File.Exists(programPath), $"Expected generated Program.cs at '{programPath}'.");

            var csprojContent = await File.ReadAllTextAsync(csprojPath);
            var programContent = await File.ReadAllTextAsync(programPath);

            Assert.Contains($"<TargetFramework>{framework}</TargetFramework>", csprojContent, StringComparison.Ordinal);
            if (useMinimalApi)
            {
                Assert.Contains("<Sdk Name=\"Microsoft.NET.Sdk.Web\" />", csprojContent, StringComparison.Ordinal);
                Assert.Contains("WebApplication.CreateBuilder(args)", programContent, StringComparison.Ordinal);
                Assert.Contains("app.MapGet(\"/\"", programContent, StringComparison.Ordinal);
                Assert.Contains("app.RunAsync()", programContent, StringComparison.Ordinal);
                Assert.DoesNotContain("Host.CreateApplicationBuilder(args)", programContent, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("<Sdk Name=\"Microsoft.NET.Sdk\" />", csprojContent, StringComparison.Ordinal);
                Assert.Contains("Host.CreateApplicationBuilder(args)", programContent, StringComparison.Ordinal);
                Assert.Contains("builder.Build().RunAsync()", programContent, StringComparison.Ordinal);
                Assert.DoesNotContain("WebApplication.CreateBuilder(args)", programContent, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("Temporal:TaskQueue", programContent, StringComparison.Ordinal);
            Assert.DoesNotContain("ResolveTaskQueue", programContent, StringComparison.Ordinal);
            Assert.Contains("builder.Services.AddHostedTemporalWorker(taskQueue)", programContent, StringComparison.Ordinal);
            Assert.Equal($"{name}-tq", ExtractQueueDefault(programContent));
            Assert.Contains(
                $"{name}.TemporalWorkerConnection.Resolve(builder.Configuration);",
                programContent, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(outputDirectory, "TemporalWorkerConnection.cs")));
            Assert.False(File.Exists(Path.Combine(outputDirectory, "TemporalConnection.cs")));

            if (includeOtel)
            {
                foreach (var source in new[] { "ClientSource", "WorkflowsSource", "ActivitiesSource", "NexusSource" })
                {
                    Assert.Contains($"TracingInterceptor.{source}.Name", programContent, StringComparison.Ordinal);
                }
                Assert.DoesNotContain("AddSource(\"Temporalio\")", programContent, StringComparison.Ordinal);
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
                $"{name}.TemporalWorkerConnection.ApplyTo(connectOptions, options);",
                programContent, StringComparison.Ordinal);
            Assert.DoesNotContain("AddSingleton<ITemporalClient>", programContent, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateLazy", programContent, StringComparison.Ordinal);
            Assert.DoesNotContain("options.LoggerFactory", programContent, StringComparison.Ordinal);

            // Register tracing once on the client; the interceptor also runs on the worker.
            AssertSingleTracingInterceptorComposition(programContent, includeOtel);

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
