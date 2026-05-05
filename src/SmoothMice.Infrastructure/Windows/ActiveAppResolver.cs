using System.IO;
using System.Text;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Resolves the executable name and elevation status of the process that owns a given HWND.
///
/// Uses <c>QueryFullProcessImageName</c> (fast, &lt;1 ms).
/// Results are cached per-HWND so Win32 calls only happen when the target window changes.
///
/// Elevation detection:
///   Non-elevated (medium-integrity) processes cannot open elevated (high-integrity)
///   processes with <c>PROCESS_QUERY_INFORMATION</c> — <c>OpenProcess</c> returns NULL.
///   We use this to detect UIPI elevation cheaply.  Elevated targets must always use
///   <c>SendInput</c> because <c>PostMessage</c> is silently dropped by UIPI.
/// </summary>
public sealed class ActiveAppResolver
{
    private IntPtr  _cachedHwnd;
    private string? _cachedExeName;
    private bool    _cachedIsElevated;

    /// <summary>Returns the exe filename (e.g. "chrome.exe") and whether the process is elevated.</summary>
    public (string? exeName, bool isElevated) TryGetWindowInfo(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return (null, false);

        if (hwnd == _cachedHwnd)
            return (_cachedExeName, _cachedIsElevated);

        _cachedHwnd = hwnd;
        QueryProcess(hwnd, out _cachedExeName, out _cachedIsElevated);
        return (_cachedExeName, _cachedIsElevated);
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
        var hFull = NativeMethods.OpenProcess(NativeMethods.ProcessQueryInformation, false, pid);
        if (hFull == IntPtr.Zero)
        {
            isElevated = true; // access denied → elevated or protected process
        }
        else
        {
            NativeMethods.CloseHandle(hFull);
        }

        // Path query: PROCESS_QUERY_LIMITED_INFORMATION works for any process.
        var hLimited = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (hLimited == IntPtr.Zero) return;

        try
        {
            var sb   = new StringBuilder(1024);
            uint sz  = (uint)sb.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(hLimited, 0, sb, ref sz)) return;
            exeName = Path.GetFileName(sb.ToString(0, (int)sz));
        }
        finally
        {
            NativeMethods.CloseHandle(hLimited);
        }
    }
}
