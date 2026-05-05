using System.IO;
using System.Text;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Resolves the executable name, elevation status, and input requirements of the
/// process / window that owns a given HWND.  All results are cached per-HWND so
/// the Win32 calls only happen when the target window changes.
///
/// <b>Elevation</b>: non-elevated (medium-integrity) processes cannot open elevated
/// (high-integrity) processes with <c>PROCESS_QUERY_INFORMATION</c> — the open fails
/// with ERROR_ACCESS_DENIED / UIPI.  <c>PostMessage</c> to an elevated window is also
/// silently dropped; <c>SendInput</c> (hardware-level injection) must be used instead.
///
/// <b>WinUI 3 / UWP</b>: modern apps built with WinUI 3 or hosted in the UWP
/// ApplicationFrame process scroll through <c>WM_POINTER</c> / <c>WM_POINTERWHEEL</c>,
/// not legacy <c>WM_MOUSEWHEEL</c>.  <c>PostMessage(WM_MOUSEWHEEL)</c> is silently
/// ignored by these apps — <c>SendInput</c> must be used because it generates authentic
/// hardware input that creates <em>both</em> the legacy and pointer-model messages.
///
/// The Windows 11 redesigned Task Manager is a WinUI 3 app and falls into this category.
/// </summary>
public sealed class ActiveAppResolver
{
    // Window root-class names that require SendInput instead of PostMessage.
    // PostMessage(WM_MOUSEWHEEL) is either blocked (UIPI) or silently ignored for these.
    private static readonly string[] _sendInputClasses =
    {
        "WinUIDesktopWin32WindowClass",  // WinUI 3 Desktop apps (Win11 Task Manager, …)
        "ApplicationFrameWindow",        // UWP app frame (Store apps hosted in shell frame)
        "Windows.UI.Core.CoreWindow",    // UWP core window (older Store apps)
    };

    private IntPtr  _cachedHwnd;
    private string? _cachedExeName;
    private bool    _cachedIsElevated;
    private bool    _cachedRequiresSendInput;

    /// <summary>
    /// Returns the exe filename, whether the process is elevated (High integrity),
    /// and whether <c>SendInput</c> must be used for injection (WinUI 3 / UWP / elevated).
    /// </summary>
    public (string? exeName, bool isElevated, bool requiresSendInput) TryGetWindowInfo(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return (null, false, false);

        if (hwnd == _cachedHwnd)
            return (_cachedExeName, _cachedIsElevated, _cachedRequiresSendInput);

        _cachedHwnd = hwnd;
        QueryWindow(hwnd,
            out _cachedExeName,
            out _cachedIsElevated,
            out _cachedRequiresSendInput);

        return (_cachedExeName, _cachedIsElevated, _cachedRequiresSendInput);
    }

    private static void QueryWindow(
        IntPtr hwnd,
        out string? exeName,
        out bool isElevated,
        out bool requiresSendInput)
    {
        exeName          = null;
        isElevated       = false;
        requiresSendInput = false;

        // ── Window-class check ──────────────────────────────────────────────────
        // Walk up to the root ancestor so we classify the app-level window, not
        // a deep child (WinUI 3 WindowFromPoint often returns a DesktopChildSiteBridge).
        var rootHwnd  = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;

        var classBuf = new StringBuilder(256);
        if (NativeMethods.GetClassName(rootHwnd, classBuf, classBuf.Capacity) > 0)
        {
            var cls = classBuf.ToString();
            foreach (var known in _sendInputClasses)
            {
                if (string.Equals(cls, known, StringComparison.OrdinalIgnoreCase))
                {
                    requiresSendInput = true;
                    break;
                }
            }
        }

        // ── Process / elevation check ───────────────────────────────────────────
        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return;

        // A medium-integrity process cannot open an elevated process with
        // PROCESS_QUERY_INFORMATION.  Failure = elevated / protected process.
        var hFull = NativeMethods.OpenProcess(NativeMethods.ProcessQueryInformation, false, pid);
        if (hFull == IntPtr.Zero)
        {
            isElevated       = true;
            requiresSendInput = true; // UIPI would silently drop PostMessage
        }
        else
        {
            NativeMethods.CloseHandle(hFull);
        }

        // Path query: PROCESS_QUERY_LIMITED_INFORMATION works for all processes.
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
