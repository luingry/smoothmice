using System.IO;
using System.Text;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Resolves the executable path of the current foreground window's process.
/// Uses QueryFullProcessImageName (fast, &lt; 1 ms) instead of Process.MainModule.FileName
/// (which enumerates all modules and can take hundreds of milliseconds, blocking the
/// low-level mouse hook callback and freezing the mouse).
/// HWND caching avoids repeated Win32 calls when the foreground window hasn't changed.
/// </summary>
public sealed class ActiveAppResolver
{
    private IntPtr _cachedHwnd;
    private string? _cachedPath;

    public string? TryGetForegroundExecutablePath()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        // Fast path: foreground window unchanged — return cached result immediately.
        if (hwnd == _cachedHwnd)
            return _cachedPath;

        _cachedHwnd = hwnd;
        _cachedPath = QueryPath(hwnd);
        return _cachedPath;
    }

    private static string? QueryPath(IntPtr hwnd)
    {
        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return null;

        var hProcess = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (hProcess == IntPtr.Zero)
            return null;

        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                return null;
            return sb.ToString(0, (int)size);
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    public static string? ExecutableNameFromPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;
        try
        {
            return Path.GetFileName(fullPath);
        }
        catch
        {
            return null;
        }
    }
}
