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
        TemporalWorkflowAnalyzer.SystemClockId,
        TemporalWorkflowAnalyzer.TaskRunId,
        TemporalWorkflowAnalyzer.NonDeterministicRandomId,
        TemporalWorkflowAnalyzer.BlockingWaitId);

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
            if (diagnostic.Id == TemporalWorkflowAnalyzer.BlockingWaitId && !IsThreadSleep(node))
            {
                continue;
            }

            var title = diagnostic.Id switch
            {
                TemporalWorkflowAnalyzer.ConfigureAwaitFalseId => "Remove ConfigureAwait(false)",
                TemporalWorkflowAnalyzer.TaskDelayId => "Use Workflow.DelayAsync",
                TemporalWorkflowAnalyzer.SystemClockId => "Use Workflow.UtcNow",
                TemporalWorkflowAnalyzer.TaskRunId => "Use Workflow.RunTaskAsync",
                TemporalWorkflowAnalyzer.NonDeterministicRandomId => "Use Temporal workflow randomness",
                TemporalWorkflowAnalyzer.BlockingWaitId => "Use Workflow.DelayAsync",
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
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return document;
        }

        ExpressionSyntax? replacement = diagnosticId switch
        {
            TemporalWorkflowAnalyzer.ConfigureAwaitFalseId => RemoveConfigureAwait(node),
            TemporalWorkflowAnalyzer.TaskDelayId => ReplaceTaskDelay(node),
            TemporalWorkflowAnalyzer.SystemClockId => SyntaxFactory.ParseExpression(
                "global::Temporalio.Workflows.Workflow.UtcNow"),
            TemporalWorkflowAnalyzer.TaskRunId => ReplaceMemberExpression(node, "RunTaskAsync"),
            TemporalWorkflowAnalyzer.NonDeterministicRandomId => ReplaceRandom(node),
            TemporalWorkflowAnalyzer.BlockingWaitId => ReplaceThreadSleep(node, semanticModel),
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

    private static InvocationExpressionSyntax? ReplaceMemberExpression(SyntaxNode node, string memberName) =>
        node is InvocationExpressionSyntax invocation &&
        invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? invocation.WithExpression(memberAccess.WithExpression(
                    SyntaxFactory.ParseExpression("global::Temporalio.Workflows.Workflow"))
                .WithName(SyntaxFactory.IdentifierName(memberName)))
            : null;

    private static ExpressionSyntax? ReplaceRandom(SyntaxNode node) => node switch
    {
        InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax memberAccess =>
            memberAccess.Name.Identifier.Text == "NewGuid"
                ? SyntaxFactory.ParseExpression("global::Temporalio.Workflows.Workflow.NewGuid()")
                : null,
        MemberAccessExpressionSyntax => SyntaxFactory.ParseExpression("global::Temporalio.Workflows.Workflow.Random"),
        ObjectCreationExpressionSyntax creation when creation.ArgumentList?.Arguments.Count is null or 0 =>
            SyntaxFactory.ParseExpression("global::Temporalio.Workflows.Workflow.Random"),
        _ => null,
    };

    private static bool IsThreadSleep(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Sleep" },
        };

    private static AwaitExpressionSyntax? ReplaceThreadSleep(SyntaxNode node, SemanticModel semanticModel) =>
        node is InvocationExpressionSyntax invocation &&
        invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
        memberAccess.Name.Identifier.Text == "Sleep" &&
        invocation.ArgumentList.Arguments.Count == 1
            ? CreateDelayAwait(
                invocation.ArgumentList.Arguments[0].Expression,
                semanticModel.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression).Type)
            : null;

    private static AwaitExpressionSyntax CreateDelayAwait(ExpressionSyntax argument, ITypeSymbol? argumentType)
    {
        var delay = argumentType?.ToDisplayString() == "System.TimeSpan"
            ? argument
            : SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression("global::System.TimeSpan.FromMilliseconds"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(argument))));

        return SyntaxFactory.AwaitExpression(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression("global::Temporalio.Workflows.Workflow.DelayAsync"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(delay)))));
    }
}
