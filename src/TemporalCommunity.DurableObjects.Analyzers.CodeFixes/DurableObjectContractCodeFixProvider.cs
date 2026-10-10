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
        DurableObjectContractAnalyzer.DeactivateOverrideMissingWorkflowUpdateId);

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

            if (diagnostic.Id == DurableObjectContractAnalyzer.InvalidContractMethodId)
            {
                var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken)
                    .ConfigureAwait(false);
                if (semanticModel is null || ResolveAttributes(method, semanticModel).Any(resolved =>
                    resolved.AttributeType.ToDisplayString() == "Temporalio.Workflows.WorkflowSignalAttribute"))
                {
                    continue;
                }
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add the required workflow handler attribute",
                    cancellationToken => ApplyFixAsync(
                        context.Document,
                        method,
                        cancellationToken),
                    equivalenceKey: diagnostic.Id),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        MethodDeclarationSyntax method,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return document;
        }

        var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
        var returnsTask = methodSymbol?.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task" ||
            methodSymbol?.ReturnType is INamedTypeSymbol named &&
            named.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>";
        var (attributeName, requiredMetadataName, conflictingMetadataName) = returnsTask
            ? ("global::Temporalio.Workflows.WorkflowUpdate", "Temporalio.Workflows.WorkflowUpdateAttribute", "Temporalio.Workflows.WorkflowQueryAttribute")
            : ("global::Temporalio.Workflows.WorkflowQuery", "Temporalio.Workflows.WorkflowQueryAttribute", "Temporalio.Workflows.WorkflowUpdateAttribute");

        // Resolve attributes semantically so aliases are recognized and unrelated attributes kept.
        var namesToRemove = new[] { conflictingMetadataName };
        var alreadyHasRequiredAttribute = ResolveAttributes(method, semanticModel)
            .Any(resolved => resolved.AttributeType.ToDisplayString() == requiredMetadataName);
        var updated = RemoveAttributesOfType(method, semanticModel, namesToRemove);
        if (!alreadyHasRequiredAttribute)
        {
            updated = updated.AddAttributeLists(SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName(attributeName)))));
        }

        updated = updated.WithAdditionalAnnotations(Formatter.Annotation);
        return document.WithSyntaxRoot(root.ReplaceNode(method, updated));
    }

    private static IEnumerable<(AttributeSyntax Syntax, INamedTypeSymbol AttributeType)> ResolveAttributes(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel) =>
        method.AttributeLists
            .SelectMany(list => list.Attributes)
            .Select(attribute => (attribute, semanticModel.GetSymbolInfo(attribute).Symbol))
            .Where(pair => pair.Item2 is IMethodSymbol { ContainingType: not null })
            .Select(pair => (pair.attribute, ((IMethodSymbol)pair.Item2!).ContainingType));

    private static MethodDeclarationSyntax RemoveAttributesOfType(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        IReadOnlyCollection<string> metadataNames)
    {
        bool IsRemoved(AttributeSyntax attribute) =>
            semanticModel.GetSymbolInfo(attribute).Symbol is IMethodSymbol { ContainingType: { } attributeType } &&
            metadataNames.Contains(attributeType.ToDisplayString());

        // Rebuild the attribute-list collection outright rather than removing individual
        // AttributeSyntax nodes: node-level removal leaves a dangling empty `[]` behind when an
        // attribute list's last attribute is removed, and chaining single-node removals across
        // more than one match silently drops every removal after the first (each subsequent node
        // reference still points at the original, already-discarded tree).
        var keptLists = method.AttributeLists
            .Select(list => list.WithAttributes(
                SyntaxFactory.SeparatedList(list.Attributes.Where(attribute => !IsRemoved(attribute)))))
            .Where(list => list.Attributes.Count > 0)
            .ToArray();
        return method.WithAttributeLists(SyntaxFactory.List(keptLists));
    }
}
