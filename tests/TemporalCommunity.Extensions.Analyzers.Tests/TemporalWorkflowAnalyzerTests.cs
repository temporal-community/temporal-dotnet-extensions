using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace TemporalCommunity.Extensions.Analyzers.Tests;

public sealed class TemporalWorkflowAnalyzerTests
{
    private const string WorkflowAttribute = """
        namespace Temporalio.Workflows
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class WorkflowAttribute : System.Attribute { }

            public static class Workflow
            {
                public static System.DateTime UtcNow => default;
                public static System.Threading.Tasks.Task DelayAsync(System.TimeSpan delay) =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
        }
        """;

    [Fact]
    public async Task ReportsConfigureAwaitFalseInsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);
                }
            }
            """);

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.ConfigureAwaitFalseId);
    }

    [Fact]
    public async Task ReportsTaskDelayInsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    await System.Threading.Tasks.Task.Delay(10);
                }
            }
            """);

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.TaskDelayId);
    }

    [Fact]
    public async Task ReportsSystemClockInsideWorkflow()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public System.DateTime Run() => System.DateTime.UtcNow;
            }
            """);

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == TemporalWorkflowAnalyzer.SystemClockId);
    }

    [Fact]
    public async Task AllowsTemporalAlternativesAndNonWorkflowCode()
    {
        var diagnostics = await AnalyzeAsync("""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    _ = Temporalio.Workflows.Workflow.UtcNow;
                    await Temporalio.Workflows.Workflow.DelayAsync(System.TimeSpan.FromSeconds(1));
                    await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(true);
                }
            }

            public sealed class ActivityCode
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    _ = System.DateTime.UtcNow;
                    await System.Threading.Tasks.Task.Delay(1).ConfigureAwait(false);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(WorkflowAttribute),
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

        var compilerErrors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(compilerErrors);

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TemporalWorkflowAnalyzer()))
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
