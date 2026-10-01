// Polyfills for C# 9+ features when targeting .NET Framework 4.8.
// This file is included in every project via Directory.Build.props.
// Do not remove: required for records, init setters, and other compiler features.

#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    // Required for `record` types and `init` setters (C# 9+).
    internal static class IsExternalInit { }
}

namespace SmoothMice
{
    /// <summary>
    /// Replacement for <c>Environment.TickCount64</c> (.NET 5+ only).
    /// Returns monotonic milliseconds since process start.
    /// </summary>
    internal static class EnvironmentEx
    {
        private static readonly long _startTs = System.Diagnostics.Stopwatch.GetTimestamp();
        private static readonly double _freqMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;

        public static long TickCount64 =>
            (long)((System.Diagnostics.Stopwatch.GetTimestamp() - _startTs) / _freqMs);
    }
}
#endif
