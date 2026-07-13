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

    [Fact]
    public async Task FixAllReplacesEveryTaskDelayInDocument()
    {
        var source = """
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    await System.Threading.Tasks.Task.Delay(10);
                    await System.Threading.Tasks.Task.Delay(20);
                }
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var fixedDocument = await ApplyFixAllAsync(
                document,
                TemporalWorkflowAnalyzer.TaskDelayId,
                new TemporalWorkflowAnalyzer(),
                new TemporalWorkflowCodeFixProvider()).ConfigureAwait(true);
            var text = (await fixedDocument.GetTextAsync().ConfigureAwait(true)).ToString();

            Assert.DoesNotContain("Task.Delay", text, StringComparison.Ordinal);
            Assert.Equal(2, text.Split("Workflow.DelayAsync", StringSplitOptions.None).Length - 1);
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

    private static async Task<Document> ApplyFixAllAsync(
        Document document,
        string diagnosticId,
        DiagnosticAnalyzer analyzer,
        CodeFixProvider provider)
    {
        var compilation = (await document.Project.GetCompilationAsync().ConfigureAwait(true))!;
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(true);
        var context = new FixAllContext(
            document,
            provider,
            FixAllScope.Document,
            diagnosticId,
            provider.FixableDiagnosticIds,
            new TestDiagnosticProvider(diagnostics),
            CancellationToken.None);
        var fixAllProvider = Assert.IsAssignableFrom<FixAllProvider>(provider.GetFixAllProvider());
        var action = await fixAllProvider.GetFixAsync(context).ConfigureAwait(true);
        var operation = Assert.IsType<ApplyChangesOperation>(Assert.Single(
            await Assert.IsAssignableFrom<CodeAction>(action)
                .GetOperationsAsync(CancellationToken.None)
                .ConfigureAwait(true)));
        return operation.ChangedSolution.GetDocument(document.Id)!;
    }

    private sealed class TestDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
        : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document,
            CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(Array.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }
}
