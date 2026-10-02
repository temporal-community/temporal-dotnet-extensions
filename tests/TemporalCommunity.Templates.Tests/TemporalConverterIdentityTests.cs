using System.Security;
using System.Text.RegularExpressions;
using Xunit;

namespace TemporalCommunity.Templates.Tests;

public sealed class TemporalConverterIdentityTests
{
    [Theory]
    [InlineData("JsonPlainConverter", null)]
    [InlineData("Foo", "FooEncoding")]
    public async Task ConverterNamesDoNotCollide(string name, string? secondName)
    {
        var project = TestFixtures.CopyHostProjectToTempDirectory();
        var hive = TestFixtures.CreateTempDirectory();
        try
        {
            await DotnetCli.RunAsync(project, "restore", "--nologo");
            await DotnetCli.RunAsync(
                project, "new", "install", RepoPaths.ContentRoot("TemporalConverter"),
                "--debug:custom-hive", hive);
            await DotnetCli.RunAsync(
                project, "new", "temporal-converter",
                "-n", name, "-o", ".", "--debug:custom-hive", hive);
            Assert.True(File.Exists(Path.Combine(project, $"{name}.cs")));

            if (secondName is not null)
            {
                await DotnetCli.RunAsync(
                    project, "new", "temporal-converter",
                    "-n", secondName, "-o", ".", "--debug:custom-hive", hive);
                Assert.True(File.Exists(Path.Combine(project, $"{secondName}.cs")));
            }

            await DotnetCli.RunAsync(project, "build", "--no-restore", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(project);
            TestFixtures.DeleteDirectory(hive);
        }
    }

    [Fact]
    public async Task EncodingIncludesGeneratedNamespaceAndName()
    {
        var firstProject = TestFixtures.CopyHostProjectToTempDirectory();
        var secondProject = TestFixtures.CopyHostProjectToTempDirectory();
        var hive = TestFixtures.CreateTempDirectory();
        try
        {
            var secondProjectPath = Path.Combine(secondProject, "HostProject.csproj");
            var secondProjectContent = await File.ReadAllTextAsync(secondProjectPath);
            await File.WriteAllTextAsync(secondProjectPath,
                secondProjectContent.Replace(
                    "<RootNamespace>Fixtures.HostProject</RootNamespace>",
                    "<RootNamespace>Fixtures.Alternate</RootNamespace>",
                    StringComparison.Ordinal));

            await DotnetCli.RunAsync(firstProject, "restore", "--nologo");
            await DotnetCli.RunAsync(secondProject, "restore", "--nologo");
            await DotnetCli.RunAsync(
                firstProject, "new", "install", RepoPaths.ContentRoot("TemporalConverter"),
                "--debug:custom-hive", hive);
            await DotnetCli.RunAsync(
                firstProject, "new", "temporal-converter",
                "-n", "FirstConverter", "-o", ".", "--debug:custom-hive", hive);
            await DotnetCli.RunAsync(
                secondProject, "new", "temporal-converter",
                "-n", "SecondConverter", "-o", ".", "--debug:custom-hive", hive);
            await DotnetCli.RunAsync(
                firstProject, "new", "temporal-converter",
                "-o", ".", "--debug:custom-hive", hive);

            var first = await File.ReadAllTextAsync(Path.Combine(firstProject, "FirstConverter.cs"));
            var second = await File.ReadAllTextAsync(Path.Combine(secondProject, "SecondConverter.cs"));
            var defaultConverter = await File.ReadAllTextAsync(
                Path.Combine(firstProject, "TemporalConverter1.cs"));

            Assert.Contains("custom/Fixtures.HostProject.FirstConverter/v1", first, StringComparison.Ordinal);
            Assert.Contains("custom/Fixtures.Alternate.SecondConverter/v1", second, StringComparison.Ordinal);
            Assert.Contains("custom/Fixtures.HostProject.TemporalConverter1/v1",
                defaultConverter, StringComparison.Ordinal);
            var fallbackEncoding = await AssertConverterFallbackAsync(
                firstProject,
                "TemporalConverter1");
            Assert.Equal("json/plain", fallbackEncoding);
            await DotnetCli.RunAsync(firstProject, "build", "--no-restore", "--nologo");
            await DotnetCli.RunAsync(secondProject, "build", "--no-restore", "--nologo");
        }
        finally
        {
            TestFixtures.DeleteDirectory(firstProject);
            TestFixtures.DeleteDirectory(secondProject);
            TestFixtures.DeleteDirectory(hive);
        }
    }

    [Fact]
    public async Task ImplementedEncodingWritesAndReadsCustomPayload()
    {
        var project = TestFixtures.CopyHostProjectToTempDirectory();
        var harness = TestFixtures.CreateTempDirectory();
        var hive = TestFixtures.CreateTempDirectory();
        try
        {
            await DotnetCli.RunAsync(project, "restore", "--nologo");
            await DotnetCli.RunAsync(
                project, "new", "install", RepoPaths.ContentRoot("TemporalConverter"),
                "--debug:custom-hive", hive);
            await DotnetCli.RunAsync(
                project, "new", "temporal-converter",
                "-n", "CustomEncodingConverter", "-o", ".", "--debug:custom-hive", hive);

            var converterPath = Path.Combine(project, "CustomEncodingConverter.cs");
            var generatedSource = await File.ReadAllTextAsync(converterPath);
            var tryToPayloadPlaceholder = Regex.Match(
                generatedSource,
                @"(?m)^            payload = null;\r?\n            return false;$");
            Assert.True(tryToPayloadPlaceholder.Success, "The generated encoding placeholder was not found.");
            generatedSource = generatedSource.Remove(
                tryToPayloadPlaceholder.Index,
                tryToPayloadPlaceholder.Length).Insert(
                    tryToPayloadPlaceholder.Index,
                    string.Join(Environment.NewLine,
                        "            if (value is not string text)",
                        "            {",
                        "                payload = null;",
                        "                return false;",
                        "            }",
                        string.Empty,
                        "            payload = new global::Temporalio.Api.Common.V1.Payload();",
                        "            payload.Metadata[\"encoding\"] = Google.Protobuf.ByteString.CopyFromUtf8(Encoding);",
                        "            payload.Data = Google.Protobuf.ByteString.CopyFromUtf8(text);",
                        "            return true;"));

            var toValuePlaceholder = Regex.Match(
                generatedSource,
                """(?m)^        public object\? ToValue\(global::Temporalio\.Api\.Common\.V1\.Payload payload, global::System\.Type type\) =>\r?\n            throw new global::System\.NotImplementedException\(\$"Replace with real deserialization logic for \{type\}\."\);$""");
            Assert.True(toValuePlaceholder.Success, "The generated ToValue placeholder was not found.");
            generatedSource = generatedSource.Remove(
                toValuePlaceholder.Index,
                toValuePlaceholder.Length).Insert(
                    toValuePlaceholder.Index,
                    string.Join(Environment.NewLine,
                        "        public object? ToValue(global::Temporalio.Api.Common.V1.Payload payload, global::System.Type type)",
                        "        {",
                        "            if (type != typeof(string))",
                        "            {",
                        "                throw new ArgumentException($\"Unsupported payload type: {type}.\", nameof(type));",
                        "            }",
                        string.Empty,
                        "            return payload.Data.ToStringUtf8();",
                        "        }"));
            await File.WriteAllTextAsync(converterPath, generatedSource);

            await WriteConverterHarnessAsync(
                harness,
                project,
                """
                using Fixtures.HostProject;

                var converter = new CustomEncodingConverter();
                var encoding = converter.EncodingConverters
                    .OfType<CustomEncodingConverter.CustomEncodingConverterEncoding>().Single();
                const string expected = "custom encoded value";
                var payload = converter.ToPayload(expected);
                if (payload.Metadata["encoding"].ToStringUtf8() != encoding.Encoding)
                {
                    throw new InvalidOperationException("Custom encoding metadata was not written.");
                }

                if (payload.Data.ToStringUtf8() != expected)
                {
                    throw new InvalidOperationException("Custom encoding payload data was not written.");
                }

                if (converter.ToValue(payload, typeof(string)) as string != expected)
                {
                    throw new InvalidOperationException("Custom encoding payload did not round-trip.");
                }

                Console.WriteLine(payload.Metadata["encoding"].ToStringUtf8());
                """);

            var output = await DotnetCli.RunAsync(harness, "run", "--project", "ConverterHarness.csproj")
                .ConfigureAwait(true);
            Assert.Contains("custom/Fixtures.HostProject.CustomEncodingConverter/v1", output, StringComparison.Ordinal);
        }
        finally
        {
            TestFixtures.DeleteDirectory(project);
            TestFixtures.DeleteDirectory(harness);
            TestFixtures.DeleteDirectory(hive);
        }
    }

    private static async Task<string> AssertConverterFallbackAsync(string project, string converterName)
    {
        var harness = TestFixtures.CreateTempDirectory();
        try
        {
            await WriteConverterHarnessAsync(
                harness,
                project,
                $$"""
                using Fixtures.HostProject;

                var converter = new {{converterName}}();
                var payload = converter.ToPayload("fallback value");
                Console.WriteLine(payload.Metadata["encoding"].ToStringUtf8());
                """).ConfigureAwait(false);
            var output = await DotnetCli.RunAsync(harness, "run", "--project", "ConverterHarness.csproj")
                .ConfigureAwait(false);
            return output.Trim().Split(Environment.NewLine).Last();
        }
        finally
        {
            TestFixtures.DeleteDirectory(harness);
        }
    }

    private static async Task WriteConverterHarnessAsync(
        string harness,
        string project,
        string program)
    {
        var projectReference = SecurityElement.Escape(Path.GetRelativePath(
            harness,
            Path.Combine(project, "HostProject.csproj")));
        await File.WriteAllTextAsync(
            Path.Combine(harness, "ConverterHarness.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{projectReference}}" />
              </ItemGroup>
            </Project>
            """).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(harness, "Program.cs"), program).ConfigureAwait(false);
    }
}
