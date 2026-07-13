using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace TemporalCommunity.Extensions.Analyzers.CodeFixes.Tests;

public sealed class TemporalWorkflowCodeFixProviderTests
{
    private const string Framework = """
        namespace Temporalio.Workflows
        {
            public sealed class WorkflowAttribute : System.Attribute { }
            public static class Workflow
            {
                public static System.DateTime UtcNow => default;
                public static System.Threading.Tasks.Task DelayAsync(int delay) =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
        }
        """;

    [Theory]
    [InlineData("await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);", "await System.Threading.Tasks.Task.CompletedTask;")]
    [InlineData("await System.Threading.Tasks.Task.Delay(10);", "await global::Temporalio.Workflows.Workflow.DelayAsync(10);")]
    [InlineData("_ = System.DateTime.UtcNow;", "_ = global::Temporalio.Workflows.Workflow.UtcNow;")]
    public async Task AppliesFixAndResultCompiles(string statement, string expected)
    {
        var source = $$"""
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    {{statement}}
                }
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
        var fixedDocument = await ApplyFirstFixAsync(document).ConfigureAwait(true);
        var text = (await fixedDocument.GetTextAsync().ConfigureAwait(true)).ToString();

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Empty((await fixedDocument.Project.GetCompilationAsync().ConfigureAwait(true))!
            .GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
    }

    private static (AdhocWorkspace Workspace, Document Document) CreateDocument(string source)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("CodeFixTests", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        project = project.AddMetadataReferences(((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)));
        project = project.AddDocument("Framework.cs", SourceText.From(Framework)).Project;
        return (workspace, project.AddDocument("Test.cs", SourceText.From(source)));
    }

    private static async Task<Document> ApplyFirstFixAsync(Document document)
    {
        var compilation = (await document.Project.GetCompilationAsync().ConfigureAwait(true))!;
        var diagnostic = Assert.Single(await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TemporalWorkflowAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(true));
        var actions = new List<CodeAction>();
        await new TemporalWorkflowCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None)).ConfigureAwait(true);
        var operation = Assert.IsType<ApplyChangesOperation>(Assert.Single(
            await Assert.Single(actions).GetOperationsAsync(CancellationToken.None).ConfigureAwait(true)));
        return operation.ChangedSolution.GetDocument(document.Id)!;
    }
}
