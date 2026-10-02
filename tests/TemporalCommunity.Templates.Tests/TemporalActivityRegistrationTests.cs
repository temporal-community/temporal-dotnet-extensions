using Xunit;

namespace TemporalCommunity.Templates.Tests;

public sealed class TemporalActivityRegistrationTests
{
    private static readonly string[] ExpectedNames = ["FirstActivity", "SecondActivity"];

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public async Task TwoNamedActivitiesInOneProjectHaveDistinctTemporalTypes(string framework)
    {
        var projectDirectory = TestFixtures.CopyHostProjectToTempDirectory();
        var hiveDirectory = TestFixtures.CreateTempDirectory();
        var harnessDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            var projectPath = Path.Combine(projectDirectory, "HostProject.csproj");
            var project = await File.ReadAllTextAsync(projectPath);
            await File.WriteAllTextAsync(
                projectPath,
                project.Replace("<TargetFramework>net10.0</TargetFramework>",
                    $"<TargetFramework>{framework}</TargetFramework>", StringComparison.Ordinal));

            await DotnetCli.RunAsync(projectDirectory, "restore", "--nologo");
            await DotnetCli.RunAsync(
                projectDirectory, "new", "install", RepoPaths.ContentRoot("TemporalActivity"),
                "--debug:custom-hive", hiveDirectory);
            foreach (var name in ExpectedNames)
            {
                await DotnetCli.RunAsync(
                    projectDirectory, "new", "temporal-activity", "-n", name, "-o", ".",
                    "--debug:custom-hive", hiveDirectory);
            }

            await DotnetCli.RunAsync(
                projectDirectory, "new", "temporal-activity", "-n", "NestedActivity",
                "-o", "Activities", "--debug:custom-hive", hiveDirectory);
            Assert.True(File.Exists(Path.Combine(projectDirectory, "Activities", "NestedActivity.cs")));

            await DotnetCli.RunAsync(
                projectDirectory, "new", "temporal-activity", "-o", ".",
                "--debug:custom-hive", hiveDirectory);
            var defaultActivity = await File.ReadAllTextAsync(
                Path.Combine(projectDirectory, "TemporalActivity1.cs"));
            Assert.Contains("TemporalActivity1Async(", defaultActivity, StringComparison.Ordinal);

            await DotnetCli.RunAsync(projectDirectory, "build", "--no-restore", "--nologo");
            await File.WriteAllTextAsync(
                Path.Combine(harnessDirectory, "Harness.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>{framework}</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Temporalio" Version="1.16.0" />
                    <Reference Include="HostProject">
                      <HintPath>{Path.Combine(projectDirectory, "bin", "Debug", framework, "HostProject.dll")}</HintPath>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(harnessDirectory, "Program.cs"),
                """
                using Fixtures.HostProject;
                using Temporalio.Activities;

                Console.WriteLine(ActivityDefinition.CreateAll(typeof(FirstActivity), new FirstActivity()).Single().Name);
                Console.WriteLine(ActivityDefinition.CreateAll(typeof(SecondActivity), new SecondActivity()).Single().Name);
                """);
            await DotnetCli.RunAsync(harnessDirectory, "restore", "--nologo");
            var names = await DotnetCli.RunAsync(
                harnessDirectory, "run", "--no-restore", "--project", "Harness.csproj");
            Assert.Equal(ExpectedNames,
                names.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            TestFixtures.DeleteDirectory(projectDirectory);
            TestFixtures.DeleteDirectory(hiveDirectory);
            TestFixtures.DeleteDirectory(harnessDirectory);
        }
    }
}
