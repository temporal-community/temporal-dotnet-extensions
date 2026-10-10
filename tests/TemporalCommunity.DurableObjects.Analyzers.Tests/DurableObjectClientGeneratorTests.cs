using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Globalization;
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

            public interface INotifications
            {
                [WorkflowSignal("inherited-notify")]
                System.Threading.Tasks.Task NotifyAsync(string? label);
            }

            [Workflow]
            public interface ICounter : IDurableObject, INotifications
            {
                [WorkflowUpdate]
                System.Threading.Tasks.Task IncrementAsync(int amount);

                [WorkflowUpdate]
                System.Threading.Tasks.Task IncrementAsync(string label);

                [WorkflowUpdate("add-value")]
                System.Threading.Tasks.Task<int> AddAsync(int amount);

                [WorkflowQuery("read-count")]
                int GetCount(string scope);

                [WorkflowSignal]
                System.Threading.Tasks.Task WakeAsync();

                [WorkflowSignal]
                System.Threading.Tasks.Task WakeAsync(int amount);

                [WorkflowSignal("send-\"message\"")]
                System.Threading.Tasks.Task SendAsync(string? @event, int @params);

                [WorkflowSignal]
                System.Threading.Tasks.Task Notify(int value);

                [WorkflowSignal]
                System.Threading.Tasks.Task @event();
            }

            public static class Consumer
            {
                public static async System.Threading.Tasks.Task Use(
                    CounterDurableObjectClient client, DurableObjectCallOptions options)
                {
                    await client.WakeAsync();
                    await client.WakeAsync(options);
                    await client.WakeAsync(2);
                    await client.WakeAsync(2, options);
                    await client.SendAsync(null, 3);
                    await client.SendAsync(null, 3, options);
                    await client.NotifyAsync(null);
                    await client.NotifyAsync(null, options);
                    await client.Notify(4, options);
                    await client.@event(options);
                    await ((ICounter)client).WakeAsync();
                }
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
        Assert.Contains("SignalAsync(\"Wake\", global::System.Array.Empty<object?>());", generated, StringComparison.Ordinal);
        Assert.Contains("SignalAsync(\"Wake\", new object?[] { amount }, callOptions);", generated, StringComparison.Ordinal);
        Assert.Contains("SignalAsync(\"send-\\\"message\\\"\", new object?[] { @event, @params }, callOptions);",
            generated, StringComparison.Ordinal);
        Assert.Contains("SignalAsync(\"inherited-notify\", new object?[] { label }, callOptions);",
            generated, StringComparison.Ordinal);
        Assert.Contains("SignalAsync(\"Notify\", new object?[] { value }, callOptions);",
            generated, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.Task @event(", generated, StringComparison.Ordinal);
        using var assembly = new MemoryStream();
        var emit = outputCompilation.Emit(assembly);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
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

    [Fact]
    public void ReportsAmbiguousAndNonMethodContractShapes()
    {
        var compilation = CreateCompilation("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public interface IFoo : IDurableObject
            {
                [WorkflowQuery] int Read();
            }

            public interface Foo : IDurableObject
            {
                [WorkflowQuery] int Read();
            }

            public interface IWithProperty : IDurableObject
            {
                int Value { get; }
            }

            public interface IOverload : IDurableObject
            {
                [WorkflowUpdate] System.Threading.Tasks.Task SaveAsync(int value);
                [WorkflowUpdate] System.Threading.Tasks.Task SaveAsync(
                    int value, DurableObjectCallOptions options);
            }
            """);
        GeneratorDriver driver = CreateDriver().RunGenerators(compilation);
        var diagnostics = driver.GetRunResult().Diagnostics
            .Where(diagnostic => diagnostic.Id == DurableObjectClientGenerator.UnsupportedContractId)
            .ToArray();

        Assert.Equal(4, diagnostics.Length);
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(
            "same generated client name", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(
            "properties and events", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(
            "call-options overload", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[WorkflowSignal] System.Threading.Tasks.Task<int> WakeAsync();", "handler shape")]
    [InlineData("[WorkflowSignal] System.Threading.Tasks.ValueTask WakeAsync();", "handler shape")]
    [InlineData("[WorkflowSignal] void Wake();", "handler shape")]
    [InlineData("[WorkflowSignal] int Wake();", "handler shape")]
    [InlineData("[WorkflowSignal, WorkflowUpdate] System.Threading.Tasks.Task WakeAsync();", "handler shape")]
    [InlineData("[WorkflowSignal, WorkflowQuery] System.Threading.Tasks.Task WakeAsync();", "handler shape")]
    [InlineData("[WorkflowSignal(Dynamic = true)] System.Threading.Tasks.Task WakeAsync();", "dynamic handler")]
    [InlineData("""
        [WorkflowSignal] System.Threading.Tasks.Task NamedAsync();
        [WorkflowSignal(Dynamic = true)] System.Threading.Tasks.Task CatchAllAsync(string name, Temporalio.Converters.IRawValue[] args);
        """, "dynamic handler")]
    [InlineData("[WorkflowSignal] System.Threading.Tasks.Task WakeAsync(int callOptions);", "parameter name")]
    [InlineData("""
        [WorkflowSignal] System.Threading.Tasks.Task WakeAsync(int value);
        [WorkflowSignal] System.Threading.Tasks.Task WakeAsync(int value, DurableObjectCallOptions options);
        """, "call-options overload")]
    [InlineData("""
        [WorkflowSignal] System.Threading.Tasks.Task ReadAsync();
        [WorkflowQuery] int Read();
        """, "generated method name")]
    public void ReportsUnsupportedSignalShapesWithoutGeneratorFailure(string members, string reason)
    {
        var compilation = CreateCompilation($$"""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;
            public interface ISignals : IDurableObject
            {
                {{members}}
            }
            """);

        var result = CreateDriver().RunGenerators(compilation).GetRunResult();
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(DurableObjectClientGenerator.UnsupportedContractId, diagnostic.Id);
        Assert.Contains(reason, diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Null(Assert.Single(result.Results).Exception);
        Assert.DoesNotContain(result.GeneratedTrees,
            tree => tree.FilePath.EndsWith("ISignals.DurableObjectClient.g.cs", StringComparison.Ordinal));
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
