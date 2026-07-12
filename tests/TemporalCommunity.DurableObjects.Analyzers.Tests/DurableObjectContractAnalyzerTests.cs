using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace TemporalCommunity.DurableObjects.Analyzers.Tests;

public sealed class DurableObjectContractAnalyzerTests
{
    private const string Framework = """
        namespace Temporalio.Workflows
        {
            public sealed class WorkflowAttribute : System.Attribute { }
            public sealed class WorkflowRunAttribute : System.Attribute { }
            public sealed class WorkflowInitAttribute : System.Attribute { }
            public sealed class WorkflowUpdateAttribute : System.Attribute { }
            public sealed class WorkflowQueryAttribute : System.Attribute { }
            public sealed class WorkflowSignalAttribute : System.Attribute { }
        }

        namespace TemporalCommunity.DurableObjects
        {
            public interface IDurableObject { }
            public abstract class DurableObjectBase { }
            public abstract class DurableObjectBase<TState> : DurableObjectBase where TState : notnull { }
            public sealed class DurableObjectSnapshot<TState> where TState : notnull { }
        }
        """;

    [Fact]
    public async Task ReportsInvalidContractShapeAndSignal()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public interface IBadObject : IDurableObject
            {
                int MissingQuery();
                [WorkflowSignal] System.Threading.Tasks.Task WakeAsync();
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.InvalidContractMethodId);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.SignalNotSupportedId);
    }

    [Fact]
    public async Task ReportsMissingRunOnConcreteObject()
    {
        var diagnostics = await AnalyzeAsync("""
            using TemporalCommunity.DurableObjects;
            public sealed class BadObject : DurableObjectBase { }
            """);

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.MissingWorkflowRunId);
    }

    [Fact]
    public async Task ReportsInvalidTypedStateSignature()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public sealed class BadObject : DurableObjectBase<int>
            {
                [WorkflowInit] public BadObject() { }
                [WorkflowRun] public System.Threading.Tasks.Task RunAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            """);

        Assert.Single(
            diagnostics,
            diagnostic => diagnostic.Id == DurableObjectContractAnalyzer.InvalidTypedStateSignatureId);
    }

    [Fact]
    public async Task AllowsValidContractAndTypedStateObject()
    {
        var diagnostics = await AnalyzeAsync("""
            using Temporalio.Workflows;
            using TemporalCommunity.DurableObjects;

            public interface ICounter : IDurableObject
            {
                [WorkflowUpdate] System.Threading.Tasks.Task IncrementAsync();
                [WorkflowQuery] int GetCount();
            }

            public sealed class Counter : DurableObjectBase<int>, ICounter
            {
                [WorkflowInit]
                public Counter(DurableObjectSnapshot<int>? snapshot = null) { }

                [WorkflowRun]
                public System.Threading.Tasks.Task RunAsync(
                    DurableObjectSnapshot<int>? snapshot = null) =>
                    System.Threading.Tasks.Task.CompletedTask;

                [WorkflowUpdate]
                public System.Threading.Tasks.Task IncrementAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;

                [WorkflowQuery]
                public int GetCount() => 0;
            }
            """);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(Framework),
            CSharpSyntaxTree.ParseText(source),
        };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "AnalyzerTests",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DurableObjectContractAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(true);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.True(diagnostic.Location.IsInSource);
        });
        return diagnostics;
    }
}
