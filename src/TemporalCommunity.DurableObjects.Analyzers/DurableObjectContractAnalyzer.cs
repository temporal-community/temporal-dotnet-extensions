using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TemporalCommunity.DurableObjects.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DurableObjectContractAnalyzer : DiagnosticAnalyzer
{
    public const string InvalidContractMethodId = "DO0001";
    public const string SignalNotSupportedId = "DO0002";
    public const string MissingWorkflowRunId = "DO0003";
    public const string InvalidTypedStateSignatureId = "DO0004";

    private static readonly DiagnosticDescriptor s_invalidContractMethod = new(
        InvalidContractMethodId,
        "DurableObject contract method has an unsupported shape",
        "Method '{0}' must be a Task-returning [WorkflowUpdate] or synchronous [WorkflowQuery]",
        "Temporal.DurableObjects",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_signalNotSupported = new(
        SignalNotSupportedId,
        "DurableObjects do not support workflow signals",
        "Method '{0}' uses [WorkflowSignal]; use [WorkflowUpdate] on DurableObjects",
        "Temporal.DurableObjects",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_missingWorkflowRun = new(
        MissingWorkflowRunId,
        "Concrete DurableObject requires a workflow run method",
        "DurableObject '{0}' must declare a method with [WorkflowRun]",
        "Temporal.DurableObjects",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_invalidTypedStateSignature = new(
        InvalidTypedStateSignatureId,
        "Typed DurableObject requires matching snapshot signatures",
        "Typed DurableObject '{0}' must declare matching [WorkflowInit] and [WorkflowRun] members with one optional DurableObjectSnapshot<TState> parameter",
        "Temporal.DurableObjects",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            s_invalidContractMethod,
            s_signalNotSupported,
            s_missingWorkflowRun,
            s_invalidTypedStateSignature);

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind == TypeKind.Interface && Implements(type, "TemporalCommunity.DurableObjects.IDurableObject"))
        {
            AnalyzeContract(context, type);
            return;
        }

        if (type.TypeKind != TypeKind.Class || type.IsAbstract ||
            !DerivesFrom(type, "TemporalCommunity.DurableObjects.DurableObjectBase"))
        {
            return;
        }

        var methods = type.GetMembers().OfType<IMethodSymbol>().ToArray();
        foreach (var method in methods.Where(method => HasAttribute(method, "Temporalio.Workflows.WorkflowSignalAttribute")))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_signalNotSupported,
                GetLocation(method),
                method.Name));
        }

        if (!methods.Any(method => HasAttribute(method, "Temporalio.Workflows.WorkflowRunAttribute")))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_missingWorkflowRun, GetLocation(type), type.Name));
        }

        var stateBase = FindGenericBase(type, "TemporalCommunity.DurableObjects.DurableObjectBase<TState>");
        if (stateBase is not null && !HasValidTypedStateSignature(type, stateBase.TypeArguments[0]))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_invalidTypedStateSignature,
                GetLocation(type),
                type.Name));
        }
    }

    private static void AnalyzeContract(SymbolAnalysisContext context, INamedTypeSymbol type)
    {
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (HasAttribute(method, "Temporalio.Workflows.WorkflowSignalAttribute"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_signalNotSupported,
                    GetLocation(method),
                    method.Name));
                continue;
            }

            var isTask = IsTask(method.ReturnType);
            var valid = isTask
                ? HasAttribute(method, "Temporalio.Workflows.WorkflowUpdateAttribute")
                : HasAttribute(method, "Temporalio.Workflows.WorkflowQueryAttribute");
            if (!valid)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_invalidContractMethod,
                    GetLocation(method),
                    method.Name));
            }
        }
    }

    private static bool HasValidTypedStateSignature(INamedTypeSymbol type, ITypeSymbol stateType)
    {
        var constructors = type.InstanceConstructors.Where(constructor =>
            HasAttribute(constructor, "Temporalio.Workflows.WorkflowInitAttribute"));
        var runMethods = type.GetMembers().OfType<IMethodSymbol>().Where(method =>
            HasAttribute(method, "Temporalio.Workflows.WorkflowRunAttribute"));
        return constructors.Any(constructor => HasSnapshotParameter(constructor, stateType)) &&
               runMethods.Any(method => HasSnapshotParameter(method, stateType));
    }

    private static bool HasSnapshotParameter(IMethodSymbol method, ITypeSymbol stateType)
    {
        if (method.Parameters.Length != 1 || !method.Parameters[0].HasExplicitDefaultValue)
        {
            return false;
        }

        return method.Parameters[0].Type is INamedTypeSymbol parameterType &&
               parameterType.OriginalDefinition.ToDisplayString() ==
                   "TemporalCommunity.DurableObjects.DurableObjectSnapshot<TState>" &&
               SymbolEqualityComparer.Default.Equals(parameterType.TypeArguments[0], stateType);
    }

    private static bool IsTask(ITypeSymbol type) =>
        type.ToDisplayString() == "System.Threading.Tasks.Task" ||
        type is INamedTypeSymbol named &&
        named.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>";

    private static bool HasAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == metadataName);

    private static bool Implements(INamedTypeSymbol type, string metadataName) =>
        type.ToDisplayString() == metadataName ||
        type.AllInterfaces.Any(@interface => @interface.ToDisplayString() == metadataName);

    private static bool DerivesFrom(INamedTypeSymbol type, string metadataName) =>
        EnumerateBaseTypes(type).Any(baseType => baseType.ToDisplayString() == metadataName);

    private static INamedTypeSymbol? FindGenericBase(INamedTypeSymbol type, string displayName) =>
        EnumerateBaseTypes(type).FirstOrDefault(baseType =>
            baseType.OriginalDefinition.ToDisplayString() == displayName);

    private static IEnumerable<INamedTypeSymbol> EnumerateBaseTypes(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static Location GetLocation(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;
}

