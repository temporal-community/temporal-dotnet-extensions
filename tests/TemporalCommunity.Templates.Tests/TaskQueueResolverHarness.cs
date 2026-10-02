using Xunit;

namespace TemporalCommunity.Templates.Tests;

internal static class TaskQueueResolverHarness
{
    public static async Task AssertContractAsync(
        string programPath,
        string expectedDefault,
        string methodName)
    {
        var source = await File.ReadAllTextAsync(programPath).ConfigureAwait(false);
        var resolver = ExtractMethod(source).Replace(
            "ResolveTaskQueue(",
            $"{methodName}(",
            StringComparison.Ordinal);
        resolver = resolver.Replace("private static string", "static string", StringComparison.Ordinal);

        var harnessDirectory = TestFixtures.CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(harnessDirectory, "Harness.csproj"),
                """
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
                </Project>
                """).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(harnessDirectory, "Program.cs"),
                $$"""
                using Microsoft.Extensions.Configuration;

                var missing = BuildConfiguration();
                var configured = BuildConfiguration(("Temporal:TaskQueue", "orders-override"));
                var blank = BuildConfiguration(("Temporal:TaskQueue", "   "));

                Console.WriteLine("MISSING|" + {{methodName}}(missing, "{{expectedDefault}}"));
                Console.WriteLine("OVERRIDE|" + {{methodName}}(configured, "{{expectedDefault}}"));
                try
                {
                    _ = {{methodName}}(blank, "{{expectedDefault}}");
                }
                catch (InvalidOperationException exception)
                {
                    Console.WriteLine("BLANK|" + exception.Message);
                }

                static IConfiguration BuildConfiguration(params (string Key, string Value)[] entries)
                {
                    var values = entries.ToDictionary(entry => entry.Key, entry => (string?)entry.Value);
                    return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
                }

                {{resolver}}
                """).ConfigureAwait(false);

            var output = await DotnetCli.RunAsync(harnessDirectory, "run", "--nologo").ConfigureAwait(false);
            Assert.Contains($"MISSING|{expectedDefault}", output, StringComparison.Ordinal);
            Assert.Contains("OVERRIDE|orders-override", output, StringComparison.Ordinal);
            Assert.Contains(
                "BLANK|Configuration value 'Temporal:TaskQueue' must not be blank. " +
                "Set it to a valid task queue name or remove it.",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            TestFixtures.DeleteDirectory(harnessDirectory);
        }
    }

    private static string ExtractMethod(string source)
    {
        var start = source.IndexOf("static string ResolveTaskQueue(", StringComparison.Ordinal);
        Assert.True(start >= 0, "Generated ResolveTaskQueue method was not found.");

        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace >= 0, "Generated ResolveTaskQueue method body was not found.");
        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException("Generated ResolveTaskQueue method was not balanced.");
    }
}
