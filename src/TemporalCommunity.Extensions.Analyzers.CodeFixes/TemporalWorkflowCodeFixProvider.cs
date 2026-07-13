using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace TemporalCommunity.Extensions.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TemporalWorkflowCodeFixProvider)), Shared]
public sealed class TemporalWorkflowCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
        TemporalWorkflowAnalyzer.ConfigureAwaitFalseId,
        TemporalWorkflowAnalyzer.TaskDelayId,
        TemporalWorkflowAnalyzer.SystemClockId);

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
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var title = diagnostic.Id switch
            {
                TemporalWorkflowAnalyzer.ConfigureAwaitFalseId => "Remove ConfigureAwait(false)",
                TemporalWorkflowAnalyzer.TaskDelayId => "Use Workflow.DelayAsync",
                TemporalWorkflowAnalyzer.SystemClockId => "Use Workflow.UtcNow",
                _ => null,
            };
            if (title is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancellationToken => ApplyFixAsync(
                        context.Document,
                        node,
                        diagnostic.Id,
                        cancellationToken),
                    equivalenceKey: diagnostic.Id),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        SyntaxNode node,
        string diagnosticId,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        ExpressionSyntax? replacement = diagnosticId switch
        {
            TemporalWorkflowAnalyzer.ConfigureAwaitFalseId => RemoveConfigureAwait(node),
            TemporalWorkflowAnalyzer.TaskDelayId => ReplaceTaskDelay(node),
            TemporalWorkflowAnalyzer.SystemClockId => SyntaxFactory.ParseExpression(
                "global::Temporalio.Workflows.Workflow.UtcNow"),
            _ => null,
        };
        if (replacement is null)
        {
            return document;
        }

        replacement = replacement
            .WithTriviaFrom(node)
            .WithAdditionalAnnotations(Formatter.Annotation);
        return document.WithSyntaxRoot(root.ReplaceNode(node, replacement));
    }

    private static ExpressionSyntax? RemoveConfigureAwait(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax memberAccess,
        }
            ? memberAccess.Expression
            : null;

    private static InvocationExpressionSyntax? ReplaceTaskDelay(SyntaxNode node) =>
        node is InvocationExpressionSyntax invocation
            ? invocation.WithExpression(SyntaxFactory.ParseExpression(
                "global::Temporalio.Workflows.Workflow.DelayAsync"))
            : null;
}

