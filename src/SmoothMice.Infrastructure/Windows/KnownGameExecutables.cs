using System.Threading;
using Microsoft.Win32;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Executables Windows itself recognized as games (Game Bar's <c>GameConfigStore</c>), plus
/// anything installed in a Steam library. Engine window classes miss proprietary engines
/// (God of War Ragnarök, The Witcher 3); these sources cover them.
/// <para>
/// The registry is read on a timer thread and swapped in atomically, so a lookup from the
/// mouse hook is a single hash-set probe and never touches the registry.
/// </para>
/// </summary>
public static class KnownGameExecutables
{
    private const string GameConfigStoreChildrenKey = @"System\GameConfigStore\Children";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(2);

    private static HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Threading.Timer RefreshTimer = new(_ => Refresh(), null, TimeSpan.Zero, RefreshInterval);

    /// <summary>True when the full executable path is a registered game or lives in a Steam library.</summary>
    public static bool Contains(string? executablePath)
    {
        GC.KeepAlive(RefreshTimer);
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        return IsInSteamLibrary(executablePath!) || Volatile.Read(ref _registered).Contains(executablePath!);
    }

    public static bool IsInSteamLibrary(string executablePath) =>
        executablePath.IndexOf(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void Refresh()
    {
        try
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var children = Registry.CurrentUser.OpenSubKey(GameConfigStoreChildrenKey);
            if (children is not null)
            {
                foreach (var name in children.GetSubKeyNames())
                {
                    using var child = children.OpenSubKey(name);
                    if (child?.GetValue("MatchedExeFullPath") is string path && !string.IsNullOrWhiteSpace(path))
                        paths.Add(path);
                }
            }

            Volatile.Write(ref _registered, paths);
        }
        catch
        {
            // Keep the previous set; game detection stays fail-open on registry errors.
        }
    }
}
