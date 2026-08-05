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
        DurableObjectContractAnalyzer.SignalNotSupportedId,
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

        var methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);
        var returnsTask = methodSymbol?.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task" ||
            methodSymbol?.ReturnType is INamedTypeSymbol named &&
            named.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>";
        var (attributeName, requiredMetadataName, conflictingMetadataName) = returnsTask
            ? ("global::Temporalio.Workflows.WorkflowUpdate", "Temporalio.Workflows.WorkflowUpdateAttribute", "Temporalio.Workflows.WorkflowQueryAttribute")
            : ("global::Temporalio.Workflows.WorkflowQuery", "Temporalio.Workflows.WorkflowQueryAttribute", "Temporalio.Workflows.WorkflowUpdateAttribute");

        // The diagnostic fires whenever the correct attribute is missing, not only when no
        // handler attribute is present at all - a method can carry a conflicting or unsupported
        // one (a signal, or the opposite update/query attribute, e.g. a Task-returning
        // DeactivateAsync override mistakenly marked [WorkflowQuery]). Adding the correct
        // attribute without removing a conflicting one would leave both on the method, which the
        // SDK rejects. A method can also already carry both [WorkflowSignal] and the required
        // attribute at once (DO0002 fires on the signal alone); in that case the required
        // attribute must be kept, not duplicated. Attributes are matched by resolving through the
        // semantic model rather than by spelling, so a `using Query = ...WorkflowQueryAttribute;`
        // alias is still caught and an unrelated attribute that merely shares a name suffix is
        // not removed.
        var namesToRemove = diagnosticId == DurableObjectContractAnalyzer.SignalNotSupportedId
            ? new[] { "Temporalio.Workflows.WorkflowSignalAttribute", conflictingMetadataName }
            : new[] { conflictingMetadataName };
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

