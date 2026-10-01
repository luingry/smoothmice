using System.IO;
using Microsoft.Win32;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Finds where an executable lives from its file name alone, so app profiles can show their icon
/// even when the app is not running. Sources, in order: a running process, then executables
/// Windows recorded as run by this user (Explorer's MuiCache and the Program Compatibility
/// Assistant store). Only existing files are returned; when several copies exist (versioned
/// install folders), the most recently written one wins. Never call from the mouse hook.
/// </summary>
public static class ExecutableLocator
{
    private const string MuiCacheKey =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
    private const string CompatibilityStoreKey =
        @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store";

    public static IReadOnlyDictionary<string, string> Locate(IEnumerable<string> executableNames)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in executableNames)
        {
            if (string.IsNullOrWhiteSpace(name) || found.ContainsKey(name))
                continue;

            var running = ActiveAppResolver.FindRunningExecutablePath(name);
            if (running is not null)
                found[name] = running;
            else
                missing.Add(name);
        }

        if (missing.Count == 0)
            return found;

        var newest = new Dictionary<string, (string Path, DateTime WrittenUtc)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in RecordedExecutablePaths())
        {
            var name = SafeFileName(path);
            if (name is null || !missing.Contains(name) || !File.Exists(path))
                continue;

            var written = File.GetLastWriteTimeUtc(path);
            if (!newest.TryGetValue(name, out var best) || written > best.WrittenUtc)
                newest[name] = (path, written);
        }

        foreach (var pair in newest)
            found[pair.Key] = pair.Value.Path;
        return found;
    }

    /// <summary>
    /// MuiCache value names are an executable path plus a suffix such as
    /// <c>.FriendlyAppName</c>; returns the path part, or null when the name holds no .exe path.
    /// </summary>
    public static string? ExecutablePathFromMuiCacheValueName(string valueName)
    {
        var end = valueName.LastIndexOf(".exe.", StringComparison.OrdinalIgnoreCase);
        if (end > 0)
            return valueName.Substring(0, end + 4);

        return valueName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? valueName : null;
    }

    private static IEnumerable<string> RecordedExecutablePaths()
    {
        foreach (var valueName in ValueNames(MuiCacheKey))
        {
            var path = ExecutablePathFromMuiCacheValueName(valueName);
            if (path is not null)
                yield return path;
        }

        foreach (var valueName in ValueNames(CompatibilityStoreKey))
        {
            if (valueName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                yield return valueName;
        }
    }

    private static string[] ValueNames(string subKey)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            return key?.GetValueNames() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? SafeFileName(string path)
    {
        try
        {
            return Path.GetFileName(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
