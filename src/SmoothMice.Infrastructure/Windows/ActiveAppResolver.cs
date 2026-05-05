using System.IO;
using System.Text;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Resolves the executable path and elevation status of the process that owns a given HWND.
///
/// Uses QueryFullProcessImageName (fast, &lt; 1 ms) rather than Process.MainModule.FileName.
/// HWND caching: if the same HWND is seen twice in a row, the cached result is returned
/// immediately — no Win32 calls at all.
///
/// Elevation detection:
///   Non-elevated (medium-integrity) processes cannot open elevated (high-integrity)
///   processes with <c>PROCESS_QUERY_INFORMATION</c> — <c>OpenProcess</c> returns NULL.
///   We use this to detect UIPI elevation cheaply, as part of the same HWND query.
///   This is important because <c>PostMessage</c> to an elevated window is silently
///   dropped by UIPI; <c>SendInput</c> must be used instead.
/// </summary>
public sealed class ActiveAppResolver
{
    private IntPtr  _cachedHwnd;
    private string? _cachedPath;
    private bool    _cachedIsElevated;

    /// <summary>
    /// Returns the executable file name (e.g. "chrome.exe") and whether the owning
    /// process runs elevated (High integrity or above).
    /// </summary>
    public (string? exeName, bool isElevated) TryGetWindowInfo(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return (null, false);

        if (hwnd == _cachedHwnd)
            return (_cachedPath, _cachedIsElevated);

        _cachedHwnd = hwnd;
        QueryProcess(hwnd, out _cachedPath, out _cachedIsElevated);
        return (_cachedPath, _cachedIsElevated);
    }

    private static void QueryProcess(IntPtr hwnd, out string? exeName, out bool isElevated)
    {
        exeName    = null;
        isElevated = false;

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return;

        // Elevation check: try to open with PROCESS_QUERY_INFORMATION.
        // A medium-integrity process CANNOT open a high-integrity (elevated) process
        // with this access right — OpenProcess returns NULL (ERROR_ACCESS_DENIED / UIPI).
        // We close the handle immediately if successful; we only need the boolean result.
        var hFull = NativeMethods.OpenProcess(NativeMethods.ProcessQueryInformation, false, pid);
        if (hFull == IntPtr.Zero)
        {
            isElevated = true; // UIPI/access denied → elevated or protected process
        }
        else
        {
            NativeMethods.CloseHandle(hFull);
        }

        // Always use the limited handle for the path query — this works regardless
        // of elevation, which is why it was added to the Windows API.
        var hLimited = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (hLimited == IntPtr.Zero) return;

        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(hLimited, 0, sb, ref size)) return;
            exeName = Path.GetFileName(sb.ToString(0, (int)size));
        }
        finally
        {
            NativeMethods.CloseHandle(hLimited);
        }
    }
}
