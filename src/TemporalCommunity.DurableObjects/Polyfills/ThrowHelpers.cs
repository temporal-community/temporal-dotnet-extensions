// Polyfill: centralized throw-helpers used throughout the library.
// Call sites use Throw.IfNull(x, nameof(x)) etc. instead of BCL static helpers
// that are unavailable on netstandard2.1 (ThrowIfNull: .NET 7+,
// ThrowIfNullOrEmpty: .NET 7+, ThrowIfNegativeOrZero: .NET 8+).
// [NotNull] on IfNull mirrors ArgumentNullException.ThrowIfNull so the compiler
// performs null-flow analysis and eliminates CS8604 warnings at call sites.

using System.Diagnostics.CodeAnalysis;

namespace TemporalCommunity.DurableObjects.Polyfills
{
    internal static class Throw
    {
#if NET7_0_OR_GREATER
        public static void IfNull([NotNull] object? argument, string? paramName = null) =>
            System.ArgumentNullException.ThrowIfNull(argument, paramName);
#else
        public static void IfNull([NotNull] object? argument, string? paramName = null)
        {
            if (argument is null)
                throw new System.ArgumentNullException(paramName);
        }
#endif

        public static void IfNullOrEmpty([NotNull] string? argument, string? paramName = null)
        {
            if (argument is null)
                throw new System.ArgumentNullException(paramName);
            if (argument.Length == 0)
                throw new System.ArgumentException("The value cannot be an empty string.", paramName);
        }

#if NET8_0_OR_GREATER
        public static void IfNegativeOrZero(int value, string? paramName = null) =>
            System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, paramName);
#else
        public static void IfNegativeOrZero(int value, string? paramName = null)
        {
            if (value <= 0)
                throw new System.ArgumentOutOfRangeException(paramName, value, "Value must be positive.");
        }
#endif
    }
}
