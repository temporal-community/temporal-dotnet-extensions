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
                public static System.Threading.Tasks.Task DelayAsync(System.TimeSpan delay) =>
                    System.Threading.Tasks.Task.CompletedTask;
                public static System.Threading.Tasks.Task RunTaskAsync(System.Func<System.Threading.Tasks.Task> func) =>
                    func();
                public static System.Guid NewGuid() => default;
                public static System.Random Random => new System.Random();
            }
        }
        """;

    [Fact]
    public void DoesNotOfferFixesForDiagnosticOnlyRules()
    {
        var provider = new TemporalWorkflowCodeFixProvider();

        Assert.DoesNotContain(TemporalWorkflowAnalyzer.SynchronizationId, provider.FixableDiagnosticIds);
        Assert.DoesNotContain(TemporalWorkflowAnalyzer.ConsoleIoId, provider.FixableDiagnosticIds);
        Assert.DoesNotContain(TemporalWorkflowAnalyzer.UnorderedCollectionId, provider.FixableDiagnosticIds);
    }

    [Theory]
    [InlineData("await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);", "await System.Threading.Tasks.Task.CompletedTask;")]
    [InlineData("await System.Threading.Tasks.Task.Delay(10);", "await global::Temporalio.Workflows.Workflow.DelayAsync(10);")]
    [InlineData("_ = System.DateTime.UtcNow;", "_ = global::Temporalio.Workflows.Workflow.UtcNow;")]
    [InlineData("await System.Threading.Tasks.Task.Run(() => System.Threading.Tasks.Task.CompletedTask);", "await global::Temporalio.Workflows.Workflow.RunTaskAsync(() => System.Threading.Tasks.Task.CompletedTask);")]
    [InlineData("_ = System.Guid.NewGuid();", "_ = global::Temporalio.Workflows.Workflow.NewGuid();")]
    [InlineData("_ = System.Random.Shared;", "_ = global::Temporalio.Workflows.Workflow.Random;")]
    [InlineData("_ = new System.Random();", "_ = global::Temporalio.Workflows.Workflow.Random;")]
    [InlineData("System.Threading.Thread.Sleep(100);", "await global::Temporalio.Workflows.Workflow.DelayAsync(global::System.TimeSpan.FromMilliseconds(100));")]
    [InlineData("System.Threading.Thread.Sleep(System.TimeSpan.FromSeconds(1));", "await global::Temporalio.Workflows.Workflow.DelayAsync(System.TimeSpan.FromSeconds(1));")]
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

    [Fact]
    public async Task UsesWorkflowTypeWhenWorkflowNamespaceIsImported()
    {
        var source = """
            using Temporalio.Workflows;

            [Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    await System.Threading.Tasks.Task.Run(() => System.Threading.Tasks.Task.CompletedTask);
                }
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var fixedDocument = await ApplyFirstFixAsync(document).ConfigureAwait(true);
            var text = (await fixedDocument.GetTextAsync().ConfigureAwait(true)).ToString();

            Assert.Contains("Workflow.RunTaskAsync", text, StringComparison.Ordinal);
            Assert.DoesNotContain("global::Temporalio.Workflows.Workflow", text, StringComparison.Ordinal);
            Assert.Empty((await fixedDocument.Project.GetCompilationAsync().ConfigureAwait(true))!
                .GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
    }

    [Fact]
    public async Task DoesNotOfferThreadSleepFixInsideSynchronousMethod()
    {
        var source = """
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public void Run()
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var actions = await GetCodeFixActionsAsync(document).ConfigureAwait(true);

            Assert.Empty(actions);
        }
    }

    [Fact]
    public async Task DoesNotOfferFixForCryptographicRandomness()
    {
        var source = """
            [Temporalio.Workflows.Workflow]
            public sealed class MyWorkflow
            {
                public void Run()
                {
                    _ = System.Security.Cryptography.RandomNumberGenerator.GetInt32(10);
                }
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var actions = await GetCodeFixActionsAsync(document).ConfigureAwait(true);

            Assert.Empty(actions);
        }
    }

    [Fact]
    public async Task UsesGlobalWorkflowNameForAliasedWorkflowNamespace()
    {
        var source = """
            using TW = Temporalio.Workflows;

            [TW.Workflow]
            public sealed class MyWorkflow
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    await System.Threading.Tasks.Task.Run(() => System.Threading.Tasks.Task.CompletedTask);
                }
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var fixedDocument = await ApplyFirstFixAsync(document).ConfigureAwait(true);
            var text = (await fixedDocument.GetTextAsync().ConfigureAwait(true)).ToString();

            Assert.Contains("global::Temporalio.Workflows.Workflow.RunTaskAsync", text, StringComparison.Ordinal);
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
        var actions = await GetCodeFixActionsAsync(document).ConfigureAwait(true);
        var operation = Assert.IsType<ApplyChangesOperation>(Assert.Single(
            await Assert.Single(actions).GetOperationsAsync(CancellationToken.None).ConfigureAwait(true)));
        return operation.ChangedSolution.GetDocument(document.Id)!;
    }

    private static async Task<List<CodeAction>> GetCodeFixActionsAsync(Document document)
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
        return actions;
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
