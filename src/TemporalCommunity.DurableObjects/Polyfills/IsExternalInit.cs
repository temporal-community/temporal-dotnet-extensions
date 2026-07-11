// Polyfill: enables C# 9 init-only setters and positional records on pre-.NET 5 targets.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
