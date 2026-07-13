using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace TemporalCommunity.DurableObjects.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DurableObjectContractCodeFixProvider)), Shared]
public sealed class DurableObjectContractCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
        DurableObjectContractAnalyzer.InvalidContractMethodId,
        DurableObjectContractAnalyzer.SignalNotSupportedId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var method = root.FindNode(diagnostic.Location.SourceSpan)
                .FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (method is null)
            {
                continue;
            }

            var title = diagnostic.Id == DurableObjectContractAnalyzer.SignalNotSupportedId
                ? "Replace signal with workflow update"
                : "Add the required workflow handler attribute";
            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancellationToken => ApplyFixAsync(
                        context.Document,
                        method,
                        diagnostic.Id,
                        cancellationToken),
                    equivalenceKey: diagnostic.Id),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        MethodDeclarationSyntax method,
        string diagnosticId,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return document;
        }

        var updated = diagnosticId == DurableObjectContractAnalyzer.SignalNotSupportedId
            ? RemoveSignalAttribute(method)
            : method;
        var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
        var returnsTask = methodSymbol?.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task" ||
            methodSymbol?.ReturnType is INamedTypeSymbol named &&
            named.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>";
        var attributeName = returnsTask
            ? "global::Temporalio.Workflows.WorkflowUpdate"
            : "global::Temporalio.Workflows.WorkflowQuery";
        updated = updated.AddAttributeLists(SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(SyntaxFactory.ParseName(attributeName)))));
        updated = updated.WithAdditionalAnnotations(Formatter.Annotation);
        return document.WithSyntaxRoot(root.ReplaceNode(method, updated));
    }

    private static MethodDeclarationSyntax RemoveSignalAttribute(MethodDeclarationSyntax method)
    {
        var attributes = method.AttributeLists
            .SelectMany(list => list.Attributes)
            .Where(attribute =>
            {
                var name = attribute.Name.ToString();
                return name.EndsWith("WorkflowSignal", StringComparison.Ordinal) ||
                       name.EndsWith("WorkflowSignalAttribute", StringComparison.Ordinal);
            })
            .ToArray();
        return attributes.Aggregate(method, (current, attribute) =>
            current.RemoveNode(attribute, SyntaxRemoveOptions.KeepNoTrivia) ?? current);
    }
}

