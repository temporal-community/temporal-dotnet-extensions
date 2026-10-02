using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Builds and runs a small harness console app that references the generated
/// <c>temporal-worker</c> project via <c>ProjectReference</c> and calls its
/// <c>TemporalWorkerConnection.Resolve</c> directly — the same "reference the generated project from a
/// separate harness assembly" mechanism <see cref="SharedTemporalConnectionResolverHarness"/> uses
/// for <c>temporal-solution</c>'s analogous <c>SharedTemporalConnection</c> helper. A
/// <c>ProjectReference</c> (rather than loading the built assembly via reflection) guarantees the
/// harness and the generated project share the exact same
/// <c>Temporalio.Client.TemporalClientConnectOptions</c> type identity, since both resolve
/// <c>Temporalio</c> through the same MSBuild build graph.
/// </summary>
internal static class TemporalConnectionResolverHarness
{
    /// <summary>
    /// Instantiates <c>temporal-worker</c> (default Framework/IncludeOtel — this template's
    /// TemporalWorkerConnection.cs content does not vary with either symbol), generates a harness project
    /// that references it, runs all five precedence scenarios in one process, and returns the
    /// parsed results.
    /// </summary>
    public static async Task<TemporalConnectionResolverRun> RunAllScenariosAsync()
    {
        var targetDirectory = TestFixtures.CreateTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        var harnessDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            const string targetName = "TemporalConnectionHarnessTarget";
            await TemporalWorkerTestHelper.InstantiateAsync(
                targetName, "net10.0", includeOtel: false, targetDirectory, settingsDirectory)
                .ConfigureAwait(false);

            var targetCsprojPath = Path.Combine(targetDirectory, $"{targetName}.csproj");
            Assert.True(File.Exists(targetCsprojPath));

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
                    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
                    <PackageReference Include="Temporalio.Extensions.Hosting" Version="1.20.0" />
                  </ItemGroup>
                  <ItemGroup>
                    <ProjectReference Include="{targetCsprojPath}" />
                  </ItemGroup>
                </Project>
                """).ConfigureAwait(false);

            await File.WriteAllTextAsync(
                Path.Combine(harnessDirectory, "Program.cs"),
                TemporalConnectionResolverHarnessProgram.Build(
                    resolverExpression: $"{targetName}.TemporalWorkerConnection.Resolve",
                    applyToExpression: $"{targetName}.TemporalWorkerConnection.ApplyTo")).ConfigureAwait(false);

            await DotnetCli.RunAsync(harnessDirectory, "build", "--nologo", "-c", "Debug").ConfigureAwait(false);

            var harnessDllPath = Path.Combine(harnessDirectory, "bin", "Debug", "net10.0", "Harness.dll");
            Assert.True(File.Exists(harnessDllPath), $"Expected built harness at '{harnessDllPath}'.");

            var stdOut = await DotnetCli.RunAsync(harnessDirectory, harnessDllPath).ConfigureAwait(false);
            return TemporalConnectionResolverHarnessProgram.ParseRun(stdOut);
        }
        finally
        {
            TestFixtures.DeleteDirectory(targetDirectory);
            TestFixtures.DeleteDirectory(settingsDirectory);
            TestFixtures.DeleteDirectory(harnessDirectory);
        }
    }
}
