using System.Collections.Concurrent;
using System.Reflection;
using Temporalio.Workflows;

namespace TemporalCommunity.DurableObjects;

/// <summary>
/// Single source of truth for resolving the Temporal workflow type name from a DurableObject
/// interface. Results are cached to avoid attribute reflection on every proxy dispatch.
/// </summary>
internal static class DurableObjectNaming
{
    private static readonly ConcurrentDictionary<Type, string> s_cache = new();

    /// <summary>
    /// Returns the Temporal workflow type name for the given DurableObject interface type.
    /// Resolution rules (applied in order):
    /// <list type="number">
    ///   <item>If the interface carries <c>[Workflow("explicit-name")]</c>, that name wins.</item>
    ///   <item>
    ///     Otherwise strip the leading <c>"I"</c> only when the next character is uppercase
    ///     (<c>ICounter</c> → <c>"Counter"</c>, <c>IHTTPServer</c> → <c>"HTTPServer"</c>,
    ///     <c>Iinterface</c> → <c>"Iinterface"</c> — not stripped).
    ///   </item>
    /// </list>
    /// </summary>
    internal static string ResolveWorkflowType(Type interfaceType) =>
        s_cache.GetOrAdd(interfaceType, Resolve);

    private static string Resolve(Type interfaceType)
    {
        // Explicit name wins — Bug 1 fix: use [Workflow("name")] on the interface, not inline I-strip.
        var attr = interfaceType.GetCustomAttribute<WorkflowAttribute>();
        if (attr?.Name is { Length: > 0 } explicitName)
            return explicitName;

        // Strip leading "I" only when the next character is also uppercase.
        var name = interfaceType.Name;
        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
            return name[1..];

        return name;
    }
}
