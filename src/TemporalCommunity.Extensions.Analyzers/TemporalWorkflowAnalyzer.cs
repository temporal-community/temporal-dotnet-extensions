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
    public const string TaskRunId = "TEMP004";
    public const string BlockingWaitId = "TEMP007";
    public const string NonDeterministicRandomId = "TEMP008";
    public const string SynchronizationId = "TEMP011";
    public const string ConsoleIoId = "TEMP013";

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

    private static readonly DiagnosticDescriptor s_taskRun = new(
        TaskRunId,
        "Do not use Task.Run in Temporal workflows",
        "Use Workflow.RunTaskAsync instead of Task.Run in workflow code",
        "Temporal.Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Task.Run uses the system task scheduler and is not deterministic under workflow replay.");

    private static readonly DiagnosticDescriptor s_blockingWait = new(
        BlockingWaitId,
        "Do not use blocking waits in Temporal workflows",
        "Use a Temporal timer or awaitable instead of {0} in workflow code",
        "Temporal.Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Thread.Sleep, Task.Wait, and timeout-based cancellation sources use system timing.");

    private static readonly DiagnosticDescriptor s_random = new(
        NonDeterministicRandomId,
        "Do not use non-deterministic random or GUID APIs in Temporal workflows",
        "Use Workflow.NewGuid or Workflow.Random instead of {0} in workflow code",
        "Temporal.Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "System, cryptographic, random, and GUID APIs are not deterministic under workflow replay.");

    private static readonly DiagnosticDescriptor s_synchronization = new(
        SynchronizationId,
        "Do not use thread synchronization primitives in Temporal workflows",
        "Use Temporal workflow coordination instead of {0} in workflow code",
        "Temporal.Determinism",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Thread synchronization primitives depend on the system scheduler and are not replay-safe.");

    private static readonly DiagnosticDescriptor s_consoleIo = new(
        ConsoleIoId,
        "Do not write directly to the console in Temporal workflows",
        "Use Workflow.Logger instead of {0} in workflow code",
        "Temporal.Observability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Direct console I/O bypasses Temporal workflow logging and can produce misleading replay output.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            s_configureAwaitFalse,
            s_taskDelay,
            s_systemClock,
            s_taskRun,
            s_blockingWait,
            s_random,
            s_synchronization,
            s_consoleIo);

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
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeLockStatement, SyntaxKind.LockStatement);
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

        if (method.Name == "Run" &&
            method.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task")
        {
            context.ReportDiagnostic(Diagnostic.Create(s_taskRun, invocation.GetLocation()));
        }

        if (method.Name == "Sleep" &&
            method.ContainingType.ToDisplayString() == "System.Threading.Thread")
        {
            context.ReportDiagnostic(Diagnostic.Create(s_blockingWait, invocation.GetLocation(), "Thread.Sleep"));
        }

        if (method.Name == "Wait" && IsTaskLike(method.ContainingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_blockingWait, invocation.GetLocation(), "Task.Wait"));
        }

        if (method.ContainingType.ToDisplayString() == "System.Threading.Monitor" &&
            method.Name is "Enter" or "TryEnter" or "Exit" or "Wait" or "Pulse" or "PulseAll")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_synchronization,
                invocation.GetLocation(),
                $"Monitor.{method.Name}"));
        }

        if (method.ContainingType.ToDisplayString() == "System.Console" &&
            method.Name is "Write" or "WriteLine")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_consoleIo,
                invocation.GetLocation(),
                $"Console.{method.Name}"));
        }

        if (method.Name == "NewGuid" &&
            method.ContainingType.ToDisplayString() == "System.Guid")
        {
            context.ReportDiagnostic(Diagnostic.Create(s_random, invocation.GetLocation(), "Guid.NewGuid"));
        }

        if (method.ContainingType.ToDisplayString() == "System.Security.Cryptography.RandomNumberGenerator" &&
            method.Name is "Create" or "Fill" or "GetBytes" or "GetNonZeroBytes" or "GetInt32" or "GetHexString")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_random,
                invocation.GetLocation(),
                $"RandomNumberGenerator.{method.Name}"));
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
        if (property is null)
        {
            return;
        }

        var containingType = property.ContainingType.ToDisplayString();
        if (property.Name == "Shared" && containingType == "System.Random")
        {
            context.ReportDiagnostic(Diagnostic.Create(s_random, memberAccess.GetLocation(), "Random.Shared"));
            return;
        }

        if (containingType == "System.Console" && property.Name is "In" or "Out" or "Error")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_consoleIo,
                memberAccess.GetLocation(),
                $"Console.{property.Name}"));
            return;
        }

        if (property.Name != "Now" && property.Name != "UtcNow")
        {
            return;
        }

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

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        if (!IsInWorkflowType(context))
        {
            return;
        }

        var creation = (ObjectCreationExpressionSyntax)context.Node;
        var type = context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is IMethodSymbol constructor
            ? constructor.ContainingType
            : context.SemanticModel.GetTypeInfo(creation.Type, context.CancellationToken).Type as INamedTypeSymbol;
        if (type?.ToDisplayString() == "System.Random")
        {
            context.ReportDiagnostic(Diagnostic.Create(s_random, creation.GetLocation(), "new Random"));
            return;
        }

        if (type?.ToDisplayString() == "System.Threading.CancellationTokenSource" &&
            creation.ArgumentList?.Arguments.Count > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_blockingWait,
                creation.GetLocation(),
                "a timeout-based CancellationTokenSource"));
        }
    }

    private static void AnalyzeLockStatement(SyntaxNodeAnalysisContext context)
    {
        if (IsInWorkflowType(context))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_synchronization, context.Node.GetLocation(), "lock"));
        }
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
