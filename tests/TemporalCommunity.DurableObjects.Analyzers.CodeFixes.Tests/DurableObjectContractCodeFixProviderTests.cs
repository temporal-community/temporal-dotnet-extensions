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
        }
        namespace TemporalCommunity.DurableObjects
        {
            public interface IDurableObject { }
        }
        """;

    [Theory]
    [InlineData(
        "[Temporalio.Workflows.WorkflowSignal] System.Threading.Tasks.Task WakeAsync();",
        "WorkflowUpdate")]
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
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DurableObjectContractAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(true));
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
}
