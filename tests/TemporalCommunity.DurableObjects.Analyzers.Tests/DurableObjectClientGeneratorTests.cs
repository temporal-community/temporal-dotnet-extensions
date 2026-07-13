using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Temporalio.Workflows;
using TemporalCommunity.DurableObjects;
using Xunit;

namespace TemporalCommunity.DurableObjects.Analyzers.Tests;

public sealed class DurableObjectClientGeneratorTests
{
    [Fact]
    public void GeneratesConcreteClientThatCompiles()
    {
        var compilation = CreateCompilation("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            namespace GeneratedConsumer;

            [Workflow]
            public interface ICounter : IDurableObject
            {
                [WorkflowUpdate]
                System.Threading.Tasks.Task IncrementAsync(int amount);

                [WorkflowUpdate("add-value")]
                System.Threading.Tasks.Task<int> AddAsync(int amount);

                [WorkflowQuery("read-count")]
                int GetCount(string scope);
            }
            """);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error));
        var generated = driver.GetRunResult().GeneratedTrees
            .Single(tree => tree.FilePath.EndsWith("ICounter.DurableObjectClient.g.cs", StringComparison.Ordinal))
            .GetText().ToString();
        Assert.Contains("public sealed class CounterDurableObjectClient", generated, StringComparison.Ordinal);
        Assert.Contains("GetCountAsync", generated, StringComparison.Ordinal);
        Assert.Contains("ExecuteUpdateAsync<global::System.Int32>(\"add-value\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"read-count\"", generated, StringComparison.Ordinal);
        Assert.Contains("DurableObjectGeneratedClientRegistry.Register", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsUnsupportedGenericContract()
    {
        var compilation = CreateCompilation("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            [Workflow]
            public interface IGeneric<T> : IDurableObject
            {
                [WorkflowQuery] T GetValue();
            }
            """);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGenerators(compilation);

        Assert.Contains(
            driver.GetRunResult().Diagnostics,
            diagnostic => diagnostic.Id == DurableObjectClientGenerator.UnsupportedContractId);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Concat(new[]
            {
                MetadataReference.CreateFromFile(typeof(IDurableObject).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(WorkflowAttribute).Assembly.Location),
            })
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
        return CSharpCompilation.Create(
            "GeneratedClientTests",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static CSharpGeneratorDriver CreateDriver() => CSharpGeneratorDriver.Create(
        new[] { new DurableObjectClientGenerator().AsSourceGenerator() },
        parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
}
