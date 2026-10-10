using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TemporalCommunity.DurableObjects.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DurableObjectContractAnalyzer : DiagnosticAnalyzer
{
    public const string InvalidContractMethodId = "DO0001";
    // Reserved for the retired signal-not-supported diagnostic; never reuse this ID.
    public const string SignalNotSupportedId = "DO0002";
    public const string MissingWorkflowRunId = "DO0003";
    public const string InvalidTypedStateSignatureId = "DO0004";
    public const string DeactivateOverrideMissingWorkflowUpdateId = "DO0007";

    private static readonly DiagnosticDescriptor s_invalidContractMethod = new(
        InvalidContractMethodId,
        "DurableObject contract method has an unsupported shape",
        "Method '{0}' must have exactly one handler attribute: Task-returning [WorkflowUpdate], synchronous [WorkflowQuery], or Task (not Task<T>)-returning named [WorkflowSignal]; dynamic signals are not supported",
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

    private static readonly DiagnosticDescriptor s_deactivateOverrideMissingWorkflowUpdate = new(
        DeactivateOverrideMissingWorkflowUpdateId,
        "DeactivateAsync override is missing [WorkflowUpdate]",
        "Method '{0}' overrides DeactivateAsync and must retain [WorkflowUpdate]",
        "Temporal.DurableObjects",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            s_invalidContractMethod,
            s_missingWorkflowRun,
            s_invalidTypedStateSignature,
            s_deactivateOverrideMissingWorkflowUpdate);

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

        if (type.TypeKind != TypeKind.Class ||
            !DerivesFrom(type, "TemporalCommunity.DurableObjects.DurableObjectBase"))
        {
            return;
        }

        // Reject dynamic signals on abstract ancestors too, so inherited dynamic handlers
        // cannot silently look supported on a concrete leaf.
        var methods = type.GetMembers().OfType<IMethodSymbol>().ToArray();
        foreach (var method in methods.Where(method =>
            HasAttribute(method, "Temporalio.Workflows.WorkflowSignalAttribute")))
        {
            if ((!type.IsAbstract || IsDynamicSignal(method)) && !HasValidHandlerShape(method))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_invalidContractMethod, GetLocation(method), method.Name));
            }
        }

        // DO0007 must catch a bad DeactivateAsync override wherever it is declared in the
        // inheritance chain, including on abstract intermediate types that never get a
        // concrete-type analysis pass of their own. Abstract types intentionally skip the
        // remaining concrete-type checks below (they may not yet have a WorkflowRun/typed-state
        // signature - that is the concrete leaf's responsibility), so DO0007 is checked here
        // and we return before reaching those checks.
        if (type.IsAbstract)
        {
            AnalyzeDeactivateOverride(context, type);
            return;
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

        AnalyzeDeactivateOverride(context, type, methods);
    }

    /// <summary>
    /// Reports DO0007 for a DeactivateAsync override declared directly on <paramref name="type"/>.
    /// This is invoked once per named type (both abstract and concrete) rather than being
    /// derived from the concrete leaf's inherited members, so the diagnostic is reported
    /// exactly once at the level where the offending override is actually declared - even
    /// when a concrete leaf never re-overrides DeactivateAsync itself and only inherits a
    /// bad override from an abstract ancestor.
    /// </summary>
    private static void AnalyzeDeactivateOverride(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        IReadOnlyList<IMethodSymbol>? declaredMethods = null)
    {
        var methods = declaredMethods ?? type.GetMembers().OfType<IMethodSymbol>().ToArray();
        var deactivateOverride = methods.FirstOrDefault(IsDeactivateAsyncOverride);
        if (deactivateOverride is not null &&
            !HasAttribute(deactivateOverride, "Temporalio.Workflows.WorkflowUpdateAttribute"))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                s_deactivateOverrideMissingWorkflowUpdate,
                GetLocation(deactivateOverride),
                deactivateOverride.Name));
        }
    }

    private static bool IsDeactivateAsyncOverride(IMethodSymbol method)
    {
        if (!method.IsOverride || method.Name != "DeactivateAsync")
        {
            return false;
        }

        for (var current = method.OverriddenMethod; current is not null; current = current.OverriddenMethod)
        {
            if (current.OverriddenMethod is null)
            {
                return current.ContainingType.ToDisplayString() ==
                    "TemporalCommunity.DurableObjects.DurableObjectBase";
            }
        }

        return false;
    }

    private static void AnalyzeContract(SymbolAnalysisContext context, INamedTypeSymbol type)
    {
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (!HasValidHandlerShape(method))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    s_invalidContractMethod,
                    GetLocation(method),
                    method.Name));
            }
        }
    }

    internal static bool HasValidHandlerShape(IMethodSymbol method)
    {
        var update = HasAttribute(method, "Temporalio.Workflows.WorkflowUpdateAttribute");
        var query = HasAttribute(method, "Temporalio.Workflows.WorkflowQueryAttribute");
        var signal = HasAttribute(method, "Temporalio.Workflows.WorkflowSignalAttribute");
        var handlerCount = method.GetAttributes().Count(attribute =>
            attribute.AttributeClass?.ToDisplayString() is
                "Temporalio.Workflows.WorkflowUpdateAttribute" or
                "Temporalio.Workflows.WorkflowQueryAttribute" or
                "Temporalio.Workflows.WorkflowSignalAttribute");
        return handlerCount == 1 &&
            (update && IsTask(method.ReturnType) ||
             query && !IsTask(method.ReturnType) ||
             signal && !IsDynamicSignal(method) &&
             method.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task");
    }

    internal static bool IsDynamicSignal(IMethodSymbol method) =>
        method.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "Temporalio.Workflows.WorkflowSignalAttribute" &&
            attribute.NamedArguments.Any(argument => argument.Key == "Dynamic" && argument.Value.Value is true));

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
