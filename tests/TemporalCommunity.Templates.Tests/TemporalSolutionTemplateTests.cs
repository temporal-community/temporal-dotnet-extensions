using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Covers the full <c>Framework</c> x <c>IncludeAspire</c> x <c>IncludeOtel</c> x <c>UseMinimalApi</c> matrix for the
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
                    foreach (var useMinimalApi in new[] { false, true })
                    {
                        yield return new object[] { framework, includeAspire, includeOtel, useMinimalApi };
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task GeneratesExpectedShapeAndBuilds(
        string framework, bool includeAspire, bool includeOtel, bool useMinimalApi)
    {
        ArgumentNullException.ThrowIfNull(framework);

        var outputDirectory = TestFixtures.CreateTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            var name = $"Sol{framework.Replace(".", string.Empty, StringComparison.Ordinal)}" +
                $"{(includeAspire ? "Aspire" : "NoAspire")}{(includeOtel ? "Otel" : "NoOtel")}" +
                $"{(useMinimalApi ? "MinimalApi" : "Console")}";

            await TemporalSolutionTestHelper.InstantiateAsync(
                name, framework, includeAspire, includeOtel, outputDirectory, settingsDirectory, useMinimalApi);

            var slnPath = Path.Combine(outputDirectory, $"{name}.sln");
            var workerCsprojPath = Path.Combine(outputDirectory, $"{name}.Worker", $"{name}.Worker.csproj");
            var clientCsprojPath = Path.Combine(outputDirectory, $"{name}.Client", $"{name}.Client.csproj");
            var sharedCsprojPath = Path.Combine(outputDirectory, $"{name}.Shared", $"{name}.Shared.csproj");
            var workerProgramPath = Path.Combine(outputDirectory, $"{name}.Worker", "Program.cs");
            var clientProgramPath = Path.Combine(outputDirectory, $"{name}.Client", "Program.cs");
            var clientDemoServicePath = Path.Combine(outputDirectory, $"{name}.Client", "DemoService.cs");
            var appHostDirectory = Path.Combine(outputDirectory, $"{name}.AppHost");
            var serviceDefaultsDirectory = Path.Combine(outputDirectory, $"{name}.ServiceDefaults");

            Assert.True(File.Exists(slnPath), $"Expected solution file at '{slnPath}'.");
            Assert.True(File.Exists(workerCsprojPath), $"Expected Worker csproj at '{workerCsprojPath}'.");
            Assert.True(File.Exists(clientCsprojPath), $"Expected Client csproj at '{clientCsprojPath}'.");
            Assert.True(File.Exists(sharedCsprojPath), $"Expected Shared csproj at '{sharedCsprojPath}'.");
            AssertTargetFramework(workerCsprojPath, framework);
            AssertTargetFramework(clientCsprojPath, framework);
            AssertTargetFramework(sharedCsprojPath, framework);

            var slnContent = await File.ReadAllTextAsync(slnPath);
            var workerProgramContent = await File.ReadAllTextAsync(workerProgramPath);
            var clientProgramContent = await File.ReadAllTextAsync(clientProgramPath);
            var clientDemoServiceContent = await File.ReadAllTextAsync(clientDemoServicePath);

            if (useMinimalApi)
            {
                var workerCsprojContent = await File.ReadAllTextAsync(workerCsprojPath);
                Assert.Contains("<Sdk Name=\"Microsoft.NET.Sdk.Web\" />", workerCsprojContent, StringComparison.Ordinal);
                Assert.Contains("WebApplication.CreateBuilder(args)", workerProgramContent, StringComparison.Ordinal);
                Assert.Contains("app.MapGet(\"/\"", workerProgramContent, StringComparison.Ordinal);
                Assert.Contains("app.RunAsync()", workerProgramContent, StringComparison.Ordinal);
                Assert.DoesNotContain("Host.CreateApplicationBuilder(args)", workerProgramContent, StringComparison.Ordinal);
            }
            else
            {
                var workerCsprojContent = await File.ReadAllTextAsync(workerCsprojPath);
                Assert.Contains("<Sdk Name=\"Microsoft.NET.Sdk\" />", workerCsprojContent, StringComparison.Ordinal);
                Assert.Contains("Host.CreateApplicationBuilder(args)", workerProgramContent, StringComparison.Ordinal);
                Assert.Contains("builder.Build().RunAsync()", workerProgramContent, StringComparison.Ordinal);
                Assert.DoesNotContain("WebApplication.CreateBuilder(args)", workerProgramContent, StringComparison.Ordinal);
            }

            Assert.Contains("Environment.ExitCode = 1;", clientDemoServiceContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Temporal:TaskQueue", workerProgramContent, StringComparison.Ordinal);
            Assert.DoesNotContain("ResolveTaskQueue", clientProgramContent + clientDemoServiceContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Temporal:TaskQueue", clientProgramContent + clientDemoServiceContent, StringComparison.Ordinal);
            Assert.DoesNotContain("class DemoService", clientProgramContent, StringComparison.Ordinal);
            Assert.DoesNotContain("ResolveTaskQueue", workerProgramContent, StringComparison.Ordinal);
            var workerQueue = ExtractQueueDefault(workerProgramContent);
            var clientQueue = ExtractQueueDefault(clientDemoServiceContent);
            var expectedQueue = $"{name}-tq";
            Assert.Equal(expectedQueue, workerQueue);
            Assert.Equal(workerQueue, clientQueue);
            Assert.Contains("stoppingToken", clientDemoServiceContent, StringComparison.Ordinal);
            Assert.Contains("OperationCanceledException", clientDemoServiceContent, StringComparison.Ordinal);
            Assert.Contains("RpcOptions", clientDemoServiceContent, StringComparison.Ordinal);

            if (includeAspire)
            {
                Assert.True(Directory.Exists(appHostDirectory), $"Expected AppHost directory at '{appHostDirectory}'.");
                Assert.True(Directory.Exists(serviceDefaultsDirectory), $"Expected ServiceDefaults directory at '{serviceDefaultsDirectory}'.");
                Assert.Contains($"{name}.AppHost", slnContent, StringComparison.Ordinal);
                Assert.Contains($"{name}.ServiceDefaults", slnContent, StringComparison.Ordinal);
                Assert.Contains("AddServiceDefaults()", workerProgramContent, StringComparison.Ordinal);
                var appHostCsprojPath = Path.Combine(appHostDirectory, $"{name}.AppHost.csproj");
                var appHostCsprojContent = await File.ReadAllTextAsync(appHostCsprojPath);
                var appHostContent = await File.ReadAllTextAsync(Path.Combine(appHostDirectory, "AppHost.cs"));
                Assert.Contains("<AspireUseCliBundle>true</AspireUseCliBundle>", appHostCsprojContent, StringComparison.Ordinal);
                AssertTargetFramework(appHostCsprojPath, framework);
                AssertTargetFramework(
                    Path.Combine(serviceDefaultsDirectory, $"{name}.ServiceDefaults.csproj"),
                    framework);

                var serviceDefaultsCsprojPath = Path.Combine(
                    serviceDefaultsDirectory, $"{name}.ServiceDefaults.csproj");
                var serviceDefaultsCsprojContent = await File.ReadAllTextAsync(serviceDefaultsCsprojPath);
                var serviceDefaultsExtensionsContent = await File.ReadAllTextAsync(
                    Path.Combine(serviceDefaultsDirectory, "Extensions.cs"));
                if (useMinimalApi)
                {
                    Assert.Contains("WithHttpHealthCheck(\"/health\")", appHostContent, StringComparison.Ordinal);
                    Assert.Contains("MapDefaultEndpoints()", workerProgramContent, StringComparison.Ordinal);
                    Assert.Contains("FrameworkReference Include=\"Microsoft.AspNetCore.App\"", serviceDefaultsCsprojContent, StringComparison.Ordinal);
                    Assert.Contains("OpenTelemetry.Instrumentation.AspNetCore", serviceDefaultsCsprojContent, StringComparison.Ordinal);
                    Assert.Contains("AddDefaultHealthChecks", serviceDefaultsExtensionsContent, StringComparison.Ordinal);
                    Assert.Contains("MapDefaultEndpoints", serviceDefaultsExtensionsContent, StringComparison.Ordinal);
                    Assert.Contains("AddAspNetCoreInstrumentation", serviceDefaultsExtensionsContent, StringComparison.Ordinal);
                }
                else
                {
                    Assert.DoesNotContain("WithHttpHealthCheck(\"/health\")", appHostContent, StringComparison.Ordinal);
                    Assert.DoesNotContain("MapDefaultEndpoints()", workerProgramContent, StringComparison.Ordinal);
                    Assert.DoesNotContain("FrameworkReference Include=\"Microsoft.AspNetCore.App\"", serviceDefaultsCsprojContent, StringComparison.Ordinal);
                    Assert.DoesNotContain("OpenTelemetry.Instrumentation.AspNetCore", serviceDefaultsCsprojContent, StringComparison.Ordinal);
                }
            }
            else
            {
                Assert.False(Directory.Exists(appHostDirectory), $"Did not expect AppHost directory at '{appHostDirectory}'.");
                Assert.False(Directory.Exists(serviceDefaultsDirectory), $"Did not expect ServiceDefaults directory at '{serviceDefaultsDirectory}'.");
                Assert.DoesNotContain($"{name}.AppHost", slnContent, StringComparison.Ordinal);
                Assert.DoesNotContain($"{name}.ServiceDefaults", slnContent, StringComparison.Ordinal);
                Assert.DoesNotContain("AddServiceDefaults()", workerProgramContent, StringComparison.Ordinal);
            }

            // Both Worker and Client register ITemporalClient through the SDK's AddTemporalClient,
            // with SharedTemporalConnection.ApplyTo transferring the resolved settings (covered by
            // SharedTemporalConnectionResolverTests), and compose TracingInterceptor exactly once.
            foreach (var programContent in new[] { workerProgramContent, clientProgramContent })
            {
                if (includeOtel)
                {
                    foreach (var source in new[] { "ClientSource", "WorkflowsSource", "ActivitiesSource", "NexusSource" })
                    {
                        Assert.Contains($"TracingInterceptor.{source}.Name", programContent, StringComparison.Ordinal);
                    }
                    Assert.DoesNotContain("AddSource(\"Temporalio\")", programContent, StringComparison.Ordinal);
                }
                else
                {
                    Assert.DoesNotContain("AddSource(\"Temporalio\")", programContent, StringComparison.Ordinal);
                }

                Assert.Contains("builder.Services.AddTemporalClient(options =>", programContent, StringComparison.Ordinal);
                Assert.Contains("SharedTemporalConnection.ApplyTo(connectOptions, options);", programContent, StringComparison.Ordinal);
                Assert.DoesNotContain("AddSingleton<ITemporalClient>", programContent, StringComparison.Ordinal);
                Assert.DoesNotContain("CreateLazy", programContent, StringComparison.Ordinal);
                Assert.DoesNotContain("options.LoggerFactory", programContent, StringComparison.Ordinal);
                TemporalWorkerTemplateTests.AssertSingleTracingInterceptorComposition(programContent, includeOtel);

                // OtelWithoutAspire tracks the standalone OTLP exporter — present only when
                // IncludeOtel=true and IncludeAspire=false; ServiceDefaults owns the exporter otherwise.
                var otelWithoutAspire = includeOtel && !includeAspire;
                if (otelWithoutAspire)
                {
                    Assert.Contains("UseOtlpExporter()", programContent, StringComparison.Ordinal);
                }
                else
                {
                    Assert.DoesNotContain("UseOtlpExporter()", programContent, StringComparison.Ordinal);
                }
            }

            // Client needs Temporalio.Extensions.Hosting for AddTemporalClient regardless of IncludeOtel.
            var clientCsprojContent = await File.ReadAllTextAsync(clientCsprojPath);
            Assert.Contains("<PackageReference Include=\"Temporalio.Extensions.Hosting\" Version=\"1.20.0\" />", clientCsprojContent, StringComparison.Ordinal);

            // Standalone build via the generated .sln — not just individual projects — to catch
            // broken ProjectReference paths from the shared sourceName token substitution.
            await DotnetCli.RunAsync(outputDirectory, "build", slnPath, "--nologo", "-m:1");

            var expectedTemporalioVersion = GetDeclaredTemporalioVersion(sharedCsprojPath);
            AssertResolvedTemporalioVersion(workerCsprojPath, expectedTemporalioVersion);
            AssertResolvedTemporalioVersion(clientCsprojPath, expectedTemporalioVersion);
            AssertResolvedTemporalioVersion(sharedCsprojPath, expectedTemporalioVersion);
            if (includeAspire)
            {
                AssertResolvedTemporalioVersion(
                    Path.Combine(appHostDirectory, $"{name}.AppHost.csproj"),
                    expectedTemporalioVersion);
                AssertNoResolvedTemporalioPackage(
                    Path.Combine(serviceDefaultsDirectory, $"{name}.ServiceDefaults.csproj"));
            }

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

            // GeneratedAspirePrefix sanitizes the hyphen and the '&' into '_' for the strongly
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

    [Theory]
    [InlineData("class", "@class", "class")]
    [InlineData("Acme.class", "Acme.@class", "Acme_class")]
    [InlineData("Contoso.Fulfillment", "Contoso.Fulfillment", "Contoso_Fulfillment")]
    [InlineData("TemporalSolution.1", "TemporalSolution._1", "TemporalSolution__1")]
    [InlineData("9Lives", "_9Lives", "_9Lives")]
    [InlineData("Acme-Orders", "Acme_Orders", "Acme_Orders")]
    [InlineData("Acme..Orders", "Acme._Orders", "Acme__Orders")]
    [InlineData("Acme.__arglist", "Acme.@__arglist", "Acme___arglist")]
    [InlineData("Acme.\u0301Orders", "Acme._\u0301Orders", "Acme_\u0301Orders")]
    [InlineData("Acme.\u203FOrders", "Acme._\u203FOrders", "Acme_\u203FOrders")]
    [InlineData("Acme.Or\u0301ders", "Acme.Or\u0301ders", "Acme_Or\u0301ders")]
    [InlineData("Acme.\u2160Orders", "Acme.\u2160Orders", "Acme__Orders")]
    [InlineData("München.Über", "München.Über", "München_Über")]
    public async Task ExplicitNamePreservesNamespaceAndUsesValidAspireProjectPrefix(
            string name,
            string namespacePrefix,
            string projectPrefix)
        {
            var outputDirectory = TestFixtures.CreateTempDirectory();
            var settingsDirectory = TestFixtures.CreateTempDirectory();
            try
            {
                await TemporalSolutionTestHelper.InstantiateAsync(
                    name, "net10.0", includeAspire: true, includeOtel: false, outputDirectory, settingsDirectory);

                var shared = await File.ReadAllTextAsync(
                    Path.Combine(outputDirectory, $"{name}.Shared", "Workflows", "SampleWorkflow.cs"));
                var worker = await File.ReadAllTextAsync(Path.Combine(outputDirectory, $"{name}.Worker", "Program.cs"));
                var client = await File.ReadAllTextAsync(Path.Combine(outputDirectory, $"{name}.Client", "Program.cs"));
                var appHost = await File.ReadAllTextAsync(Path.Combine(outputDirectory, $"{name}.AppHost", "AppHost.cs"));

                Assert.Contains($"namespace {namespacePrefix}.Shared", shared, StringComparison.Ordinal);
                Assert.Contains($"using {namespacePrefix}.Shared", worker, StringComparison.Ordinal);
                Assert.Contains($"using {namespacePrefix}.Shared", client, StringComparison.Ordinal);
                Assert.Contains($"Projects.{projectPrefix}_Worker", appHost, StringComparison.Ordinal);
                Assert.Contains($"Projects.{projectPrefix}_Client", appHost, StringComparison.Ordinal);

                await DotnetCli.RunAsync(
                    outputDirectory,
                    "build",
                    Path.Combine(outputDirectory, $"{name}.sln"),
                    "--nologo",
                    "-m:1");
            }

            finally
            {
                TestFixtures.DeleteDirectory(outputDirectory);
                TestFixtures.DeleteDirectory(settingsDirectory);
            }
    }

    [Fact]
    public async Task OmittingNameUsesOutputDirectoryNameInsteadOfTemplateDefaultName()
    {
        var rootDirectory = TestFixtures.CreateTempDirectory();
        var outputDirectory = Path.Combine(rootDirectory, "DirectoryDerivedSolution");
        var hiveDirectory = Path.Combine(rootDirectory, "hive");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            await DotnetCli.RunAsync(
                rootDirectory,
                "new", "install", RepoPaths.ContentRoot("TemporalSolution"),
                "--debug:custom-hive", hiveDirectory);
            await DotnetCli.RunAsync(
                rootDirectory,
                "new", "temporal-solution",
                "-o", outputDirectory,
                "--framework", "net10.0",
                "--aspire", "false",
                "--otel", "false",
                "--debug:custom-hive", hiveDirectory);

            Assert.True(Directory.Exists(Path.Combine(outputDirectory, "DirectoryDerivedSolution.Worker")));
            Assert.False(Directory.Exists(Path.Combine(outputDirectory, "TemporalSolution.1.Worker")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "DirectoryDerivedSolution.sln")));
            var workerProgram = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "DirectoryDerivedSolution.Worker", "Program.cs"));
            var clientProgram = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "DirectoryDerivedSolution.Client", "DemoService.cs"));
            Assert.Equal("DirectoryDerivedSolution-tq", ExtractQueueDefault(workerProgram));
            Assert.Equal(ExtractQueueDefault(workerProgram), ExtractQueueDefault(clientProgram));
        }
        finally
        {
            TestFixtures.DeleteDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task DistinctSolutionNamesGenerateDistinctMatchingQueueDefaults()
    {
        var alphaDirectory = TestFixtures.CreateTempDirectory();
        var betaDirectory = TestFixtures.CreateTempDirectory();
        var alphaSettings = TestFixtures.CreateTempDirectory();
        var betaSettings = TestFixtures.CreateTempDirectory();
        try
        {
            await TemporalSolutionTestHelper.InstantiateAsync(
                "Alpha", "net10.0", includeAspire: false, includeOtel: false, alphaDirectory, alphaSettings);
            await TemporalSolutionTestHelper.InstantiateAsync(
                "Beta", "net10.0", includeAspire: false, includeOtel: false, betaDirectory, betaSettings);

            var alphaWorker = await File.ReadAllTextAsync(Path.Combine(alphaDirectory, "Alpha.Worker", "Program.cs"));
            var alphaClient = await File.ReadAllTextAsync(Path.Combine(alphaDirectory, "Alpha.Client", "DemoService.cs"));
            var betaWorker = await File.ReadAllTextAsync(Path.Combine(betaDirectory, "Beta.Worker", "Program.cs"));
            var betaClient = await File.ReadAllTextAsync(Path.Combine(betaDirectory, "Beta.Client", "DemoService.cs"));

            Assert.Equal("Alpha-tq", ExtractQueueDefault(alphaWorker));
            Assert.Equal(ExtractQueueDefault(alphaWorker), ExtractQueueDefault(alphaClient));
            Assert.Equal("Beta-tq", ExtractQueueDefault(betaWorker));
            Assert.Equal(ExtractQueueDefault(betaWorker), ExtractQueueDefault(betaClient));
            Assert.NotEqual(ExtractQueueDefault(alphaWorker), ExtractQueueDefault(betaWorker));
        }
        finally
        {
            TestFixtures.DeleteDirectory(alphaDirectory);
            TestFixtures.DeleteDirectory(betaDirectory);
            TestFixtures.DeleteDirectory(alphaSettings);
            TestFixtures.DeleteDirectory(betaSettings);
        }
    }

    private static string ExtractQueueDefault(string content)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            "(?:const string taskQueue|TaskQueue)\\s*=\\s*\"(?<queue>[^\"]+)\"");
        Assert.True(match.Success, "Generated queue default was not found.");
        return match.Groups["queue"].Value;
    }

    private static void AssertTargetFramework(string projectPath, string expectedFramework)
    {
        var actualFramework = System.Xml.Linq.XDocument.Load(projectPath)
            .Descendants("TargetFramework")
            .Single()
            .Value;
        Assert.Equal(expectedFramework, actualFramework);
    }

    private static string GetDeclaredTemporalioVersion(string projectPath)
    {
        var temporalioReference = System.Xml.Linq.XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Single(element => (string?)element.Attribute("Include") == "Temporalio");
        return (string?)temporalioReference.Attribute("Version")
            ?? throw new InvalidOperationException($"Temporalio PackageReference in '{projectPath}' has no Version.");
    }

    private static void AssertResolvedTemporalioVersion(string projectPath, string expectedVersion)
    {
        var resolvedLibrary = GetResolvedTemporalioLibraries(projectPath).Single();
        Assert.Equal($"Temporalio/{expectedVersion}", resolvedLibrary);
    }

    private static void AssertNoResolvedTemporalioPackage(string projectPath)
    {
        Assert.Empty(GetResolvedTemporalioLibraries(projectPath));
    }

    private static string[] GetResolvedTemporalioLibraries(string projectPath)
    {
        var assetsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json");
        using var assets = System.Text.Json.JsonDocument.Parse(File.ReadAllText(assetsPath));
        return assets.RootElement
            .GetProperty("libraries")
            .EnumerateObject()
            .Select(library => library.Name)
            .Where(name => name.StartsWith("Temporalio/", StringComparison.Ordinal))
            .ToArray();
    }
}
