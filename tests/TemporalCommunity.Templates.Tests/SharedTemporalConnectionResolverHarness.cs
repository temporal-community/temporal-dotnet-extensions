using Xunit;

namespace TemporalCommunity.Templates.Tests;

/// <summary>
/// Builds and runs a small harness console app that references the generated
/// <c>temporal-solution</c> Shared project via <c>ProjectReference</c> and calls its
/// <c>SharedTemporalConnection.Resolve</c> directly. Mirrors
/// <see cref="TemporalConnectionResolverHarness"/>'s approach for <c>temporal-worker</c>.
/// </summary>
internal static class SharedTemporalConnectionResolverHarness
{
    public static async Task<IReadOnlyDictionary<string, TemporalConnectionResolverResult>> RunAllScenariosAsync()
    {
        var targetDirectory = TestFixtures.CreateTempDirectory();
        var settingsDirectory = TestFixtures.CreateTempDirectory();
        var harnessDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            const string targetName = "SharedConnectionHarnessTarget";
            await TemporalSolutionTestHelper.InstantiateAsync(
                targetName, "net10.0", includeAspire: false, includeOtel: false, targetDirectory, settingsDirectory)
                .ConfigureAwait(false);

            var sharedCsprojPath = Path.Combine(targetDirectory, $"{targetName}.Shared", $"{targetName}.Shared.csproj");
            Assert.True(File.Exists(sharedCsprojPath));

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
                  </ItemGroup>
                  <ItemGroup>
                    <ProjectReference Include="{sharedCsprojPath}" />
                  </ItemGroup>
                </Project>
                """).ConfigureAwait(false);

            await File.WriteAllTextAsync(
                Path.Combine(harnessDirectory, "Program.cs"),
                TemporalConnectionResolverHarnessProgram.Build(
                    resolverExpression: $"{targetName}.Shared.SharedTemporalConnection.Resolve")).ConfigureAwait(false);

            await DotnetCli.RunAsync(harnessDirectory, "build", "--nologo", "-c", "Debug").ConfigureAwait(false);

            var harnessDllPath = Path.Combine(harnessDirectory, "bin", "Debug", "net10.0", "Harness.dll");
            Assert.True(File.Exists(harnessDllPath), $"Expected built harness at '{harnessDllPath}'.");

            var stdOut = await DotnetCli.RunAsync(harnessDirectory, harnessDllPath).ConfigureAwait(false);
            return TemporalConnectionResolverHarnessProgram.ParseResults(stdOut);
        }
        finally
        {
            TestFixtures.DeleteDirectory(targetDirectory);
            TestFixtures.DeleteDirectory(settingsDirectory);
            TestFixtures.DeleteDirectory(harnessDirectory);
        }
    }
}
