using System.IO;
using System.Text;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Resolves the executable path of the process that owns a given window handle.
///
/// Uses QueryFullProcessImageName (fast, &lt; 1 ms) rather than Process.MainModule.FileName
/// (which enumerates all modules and can take hundreds of milliseconds, blocking the
/// low-level mouse hook callback and freezing the mouse cursor).
///
/// HWND caching: if the same HWND is seen twice in a row, the cached path is returned
/// immediately — no Win32 calls at all.
///
/// Usage in the scroll pipeline:
///   Always resolve against the window UNDER THE CURSOR (<c>WindowFromPoint</c>),
///   not the foreground window (<c>GetForegroundWindow</c>).  Scroll events are
///   delivered to the window under the cursor regardless of keyboard focus, so
///   profile resolution must follow the same window.
/// </summary>
public sealed class ActiveAppResolver
{
    private IntPtr  _cachedHwnd;
    private string? _cachedPath;

    /// <summary>
    /// Returns the full executable path of the process that owns <paramref name="hwnd"/>.
    /// Returns <c>null</c> if the handle is zero or the query fails.
    /// </summary>
    public string? TryGetExecutablePathForHwnd(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;

        // Fast path: same window as last call — no Win32 round-trip needed.
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
