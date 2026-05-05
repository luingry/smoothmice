// Polyfills para C# 9+ features quando targeting .NET Framework 4.8.
// Este ficheiro é incluído em todos os projetos via Directory.Build.props.
// Não remover: necessário para records, init setters, e outros features do compilador.

#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    // Necessário para `record` types e `init` setters (C# 9+).
    internal static class IsExternalInit { }
}

namespace SmoothMice
{
    /// <summary>
    /// Substituto de <c>Environment.TickCount64</c> (apenas .NET 5+).
    /// Devolve millisegundos monotónicos desde o arranque do processo.
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
