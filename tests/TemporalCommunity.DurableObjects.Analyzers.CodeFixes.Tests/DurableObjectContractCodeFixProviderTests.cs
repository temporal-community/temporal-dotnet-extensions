using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace TemporalCommunity.DurableObjects.Analyzers.CodeFixes.Tests;

public sealed class DurableObjectContractCodeFixProviderTests
{
    private const string Framework = """
        namespace Temporalio.Workflows
        {
            public sealed class WorkflowUpdateAttribute : System.Attribute { }
            public sealed class WorkflowQueryAttribute : System.Attribute { }
            public sealed class WorkflowSignalAttribute : System.Attribute { }
            public sealed class WorkflowRunAttribute : System.Attribute { }
        }
        namespace TemporalCommunity.DurableObjects
        {
            public interface IDurableObject { }
            public abstract class DurableObjectBase
            {
                [Temporalio.Workflows.WorkflowUpdate]
                public virtual System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
        }
        """;

    [Theory]
    [InlineData(
        "System.Threading.Tasks.Task IncrementAsync();",
        "WorkflowUpdate")]
    [InlineData("int GetCount();", "WorkflowQuery")]
    public async Task AppliesContractFixAndResultCompiles(string member, string expectedAttribute)
    {
        var source = $$"""
            public interface ICounter : TemporalCommunity.DurableObjects.IDurableObject
            {
                {{member}}
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var fixedDocument = await ApplyFirstFixAsync(document).ConfigureAwait(true);
            var text = (await fixedDocument.GetTextAsync().ConfigureAwait(true)).ToString();

            Assert.Contains(expectedAttribute, text, StringComparison.Ordinal);
            Assert.Empty((await fixedDocument.Project.GetCompilationAsync().ConfigureAwait(true))!
                .GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
    }

    [Fact]
    public async Task AppliesDeactivateAsyncFixAndResultCompiles()
    {
        var source = """
            public sealed class Counter : TemporalCommunity.DurableObjects.DurableObjectBase
            {
                [Temporalio.Workflows.WorkflowRun]
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;

                public override System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var fixedDocument = await ApplyFirstFixAsync(document).ConfigureAwait(true);
            var text = (await fixedDocument.GetTextAsync().ConfigureAwait(true)).ToString();

            Assert.Contains("WorkflowUpdate", text, StringComparison.Ordinal);
            Assert.Empty((await fixedDocument.Project.GetCompilationAsync().ConfigureAwait(true))!
                .GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
    }

    [Fact]
    public async Task DeactivateAsyncFixRemovesConflictingWorkflowQueryAttribute()
    {
        var source = """
            public sealed class Counter : TemporalCommunity.DurableObjects.DurableObjectBase
            {
                [Temporalio.Workflows.WorkflowRun]
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;

                [Temporalio.Workflows.WorkflowQuery]
                public override System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var fixedDocument = await ApplyFirstFixAsync(document).ConfigureAwait(true);
            var text = (await fixedDocument.GetTextAsync().ConfigureAwait(true)).ToString();

            Assert.Contains("WorkflowUpdate", text, StringComparison.Ordinal);
            Assert.DoesNotContain("WorkflowQuery", text, StringComparison.Ordinal);
            Assert.Empty((await fixedDocument.Project.GetCompilationAsync().ConfigureAwait(true))!
                .GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
    }

    [Fact]
    public async Task ContractFixRemovesConflictingAttributeOnInterfaceMethod()
    {
        var source = """
            public interface ICounter : TemporalCommunity.DurableObjects.IDurableObject
            {
                [Temporalio.Workflows.WorkflowQuery]
                System.Threading.Tasks.Task IncrementAsync();
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var fixedDocument = await ApplyFirstFixAsync(document).ConfigureAwait(true);
            var text = (await fixedDocument.GetTextAsync().ConfigureAwait(true)).ToString();

            Assert.Contains("WorkflowUpdate", text, StringComparison.Ordinal);
            Assert.DoesNotContain("WorkflowQuery", text, StringComparison.Ordinal);
            Assert.Empty((await fixedDocument.Project.GetCompilationAsync().ConfigureAwait(true))!
                .GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
    }

    [Fact]
    public async Task DeactivateAsyncFixRemovesConflictingAttributeAccessedThroughAlias()
    {
        var source = """
            using Query = Temporalio.Workflows.WorkflowQueryAttribute;

            public sealed class Counter : TemporalCommunity.DurableObjects.DurableObjectBase
            {
                [Temporalio.Workflows.WorkflowRun]
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;

                [Query]
                public override System.Threading.Tasks.Task DeactivateAsync() =>
                    System.Threading.Tasks.Task.CompletedTask;
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var fixedDocument = await ApplyFirstFixAsync(document).ConfigureAwait(true);
            var text = (await fixedDocument.GetTextAsync().ConfigureAwait(true)).ToString();

            Assert.Contains("WorkflowUpdate", text, StringComparison.Ordinal);
            Assert.DoesNotContain("[Query]", text, StringComparison.Ordinal);
            Assert.Empty((await fixedDocument.Project.GetCompilationAsync().ConfigureAwait(true))!
                .GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
    }

    [Fact]
    public async Task ContractFixDoesNotRemoveUnrelatedAttributeSharingNameSuffix()
    {
        var source = """
            public sealed class MyWorkflowQueryAttribute : System.Attribute { }

            public interface ICounter : TemporalCommunity.DurableObjects.IDurableObject
            {
                [MyWorkflowQuery]
                System.Threading.Tasks.Task IncrementAsync();
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var fixedDocument = await ApplyFirstFixAsync(document).ConfigureAwait(true);
            var text = (await fixedDocument.GetTextAsync().ConfigureAwait(true)).ToString();

            Assert.Contains("WorkflowUpdate", text, StringComparison.Ordinal);
            Assert.Contains("[MyWorkflowQuery]", text, StringComparison.Ordinal);
            Assert.Empty((await fixedDocument.Project.GetCompilationAsync().ConfigureAwait(true))!
                .GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        }
    }

    [Theory]
    [InlineData("[Signal] System.Threading.Tasks.Task<int> WakeAsync();")]
    [InlineData("[Signal, Temporalio.Workflows.WorkflowUpdate] System.Threading.Tasks.Task WakeAsync();")]
    [InlineData("[Signal, Temporalio.Workflows.WorkflowQuery] int Wake();")]
    public async Task DoesNotOfferSignalToUpdateFix(string member)
    {
        var source = $$"""
            using Signal = Temporalio.Workflows.WorkflowSignalAttribute;

            public interface ICounter : TemporalCommunity.DurableObjects.IDurableObject
            {
                {{member}}
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var diagnostic = Assert.Single(await GetAnalyzerDiagnosticsAsync(document).ConfigureAwait(true));
            var provider = new DurableObjectContractCodeFixProvider();
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(
                document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None))
                .ConfigureAwait(true);

            Assert.Equal(DurableObjectContractAnalyzer.InvalidContractMethodId, diagnostic.Id);
            Assert.Empty(actions);
            Assert.DoesNotContain(DurableObjectContractAnalyzer.SignalNotSupportedId, provider.FixableDiagnosticIds);
        }
    }

    [Fact]
    public async Task ValidSignalNeedsNoFix()
    {
        var (workspace, document) = CreateDocument("""
            public interface ICounter : TemporalCommunity.DurableObjects.IDurableObject
            {
                [Temporalio.Workflows.WorkflowSignal] System.Threading.Tasks.Task WakeAsync();
            }
            """);
        using (workspace)
        {
            Assert.Empty(await GetAnalyzerDiagnosticsAsync(document).ConfigureAwait(true));
        }
    }

    [Fact]
    public async Task FixAllAddsAttributesToEveryInvalidContractMethod()
    {
        var source = """
            public interface ICounter : TemporalCommunity.DurableObjects.IDurableObject
            {
                System.Threading.Tasks.Task IncrementAsync();
                System.Threading.Tasks.Task ResetAsync();
            }
            """;
        var (workspace, document) = CreateDocument(source);
        using (workspace)
        {
            var compilation = (await document.Project.GetCompilationAsync().ConfigureAwait(true))!;
            var diagnostics = await compilation
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DurableObjectContractAnalyzer()))
                .GetAnalyzerDiagnosticsAsync()
                .ConfigureAwait(true);
            var provider = new DurableObjectContractCodeFixProvider();
            var context = new FixAllContext(
                document,
                provider,
                FixAllScope.Document,
                DurableObjectContractAnalyzer.InvalidContractMethodId,
                provider.FixableDiagnosticIds,
                new TestDiagnosticProvider(diagnostics),
                CancellationToken.None);
            var fixAllProvider = Assert.IsAssignableFrom<FixAllProvider>(provider.GetFixAllProvider());
            var action = await fixAllProvider.GetFixAsync(context).ConfigureAwait(true);
            var operation = Assert.IsType<ApplyChangesOperation>(Assert.Single(
                await Assert.IsAssignableFrom<CodeAction>(action)
                    .GetOperationsAsync(CancellationToken.None)
                    .ConfigureAwait(true)));
            var fixedDocument = operation.ChangedSolution.GetDocument(document.Id)!;
            var text = (await fixedDocument.GetTextAsync().ConfigureAwait(true)).ToString();

            Assert.Equal(2, text.Split("WorkflowUpdate", StringSplitOptions.None).Length - 1);
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

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(Document document)
    {
        var compilation = (await document.Project.GetCompilationAsync().ConfigureAwait(true))!;
        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DurableObjectContractAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(true);
    }

    private static async Task<Document> ApplyFirstFixAsync(Document document)
    {
        var diagnostic = Assert.Single(await GetAnalyzerDiagnosticsAsync(document).ConfigureAwait(true));
        var actions = new List<CodeAction>();
        await new DurableObjectContractCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None)).ConfigureAwait(true);
        var operation = Assert.IsType<ApplyChangesOperation>(Assert.Single(
            await Assert.Single(actions).GetOperationsAsync(CancellationToken.None).ConfigureAwait(true)));
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
