using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TemporalCommunity.Extensions.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TemporalWorkflowAnalyzer : DiagnosticAnalyzer
{
    public const string ConfigureAwaitFalseId = "TEMP001";
    public const string TaskDelayId = "TEMP002";
    public const string SystemClockId = "TEMP003";

    private static readonly DiagnosticDescriptor s_configureAwaitFalse = new(
        ConfigureAwaitFalseId,
        "Do not use ConfigureAwait(false) in Temporal workflows",
        "Workflow code must remain on Temporal's task scheduler; remove ConfigureAwait(false)",
        "Temporal.Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Temporal workflows require TaskScheduler.Current. ConfigureAwait(false) uses the default scheduler.");

    private static readonly DiagnosticDescriptor s_taskDelay = new(
        TaskDelayId,
        "Do not use Task.Delay in Temporal workflows",
        "Use Workflow.DelayAsync instead of Task.Delay in workflow code",
        "Temporal.Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Task.Delay uses a system timer and is not deterministic under workflow replay.");

    private static readonly DiagnosticDescriptor s_systemClock = new(
        SystemClockId,
        "Do not use the system clock in Temporal workflows",
        "Use Workflow.UtcNow instead of {0}.{1} in workflow code",
        "Temporal.Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "System clock reads are not deterministic under workflow replay.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(s_configureAwaitFalse, s_taskDelay, s_systemClock);

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (!IsInWorkflowType(context))
        {
            return;
        }

        var invocation = (InvocationExpressionSyntax)context.Node;
        var method = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
        if (method is null)
        {
            return;
        }

        if (method.Name == "ConfigureAwait" &&
            IsTaskLike(method.ContainingType) &&
            invocation.ArgumentList.Arguments.Count == 1)
        {
            var constant = context.SemanticModel.GetConstantValue(
                invocation.ArgumentList.Arguments[0].Expression,
                context.CancellationToken);
            if (constant is { HasValue: true, Value: false })
            {
                context.ReportDiagnostic(Diagnostic.Create(s_configureAwaitFalse, invocation.GetLocation()));
            }
        }

        if (method.Name == "Delay" &&
            method.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task")
        {
            context.ReportDiagnostic(Diagnostic.Create(s_taskDelay, invocation.GetLocation()));
        }
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        if (!IsInWorkflowType(context))
        {
            return;
        }

        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var property = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol as IPropertySymbol;
        if (property is null || (property.Name != "Now" && property.Name != "UtcNow"))
        {
            return;
        }

        var containingType = property.ContainingType.ToDisplayString();
        if (containingType != "System.DateTime" && containingType != "System.DateTimeOffset")
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            s_systemClock,
            memberAccess.GetLocation(),
            property.ContainingType.Name,
            property.Name));
    }

    private static bool IsInWorkflowType(SyntaxNodeAnalysisContext context)
    {
        var typeDeclaration = context.Node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDeclaration is null)
        {
            return false;
        }

        var type = context.SemanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken);
        return type?.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "Temporalio.Workflows.WorkflowAttribute") == true;
    }

    private static bool IsTaskLike(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition.ToDisplayString();
        return definition == "System.Threading.Tasks.Task" ||
               definition == "System.Threading.Tasks.Task<TResult>" ||
               definition == "System.Threading.Tasks.ValueTask" ||
               definition == "System.Threading.Tasks.ValueTask<TResult>";
    }
}
