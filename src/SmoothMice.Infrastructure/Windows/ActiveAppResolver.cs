using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Resolves the executable name, elevation status, and scroll-control type for a given HWND.
///
/// Results are cached per-HWND (fast path: same HWND = zero Win32 calls).
///
/// <b>Elevation</b>: non-elevated processes cannot open elevated ones with
/// <c>PROCESS_QUERY_INFORMATION</c> — we use this to detect UIPI targets cheaply.
///
/// <b>Legacy scroll controls</b> (see <c>_legacyScrollClasses</c>): Win32 controls such as
/// <c>DirectUIHWND</c> (Explorer folder view) and <c>SysListView32</c> only respond to full
/// WHEEL_DELTA (120-unit) inputs.  Injecting our sub-120 smooth values causes a "stall then
/// jump" effect.  The <c>isLegacyScrollControl</c> flag tells the coordinator to pass through
/// the original event unchanged (native scroll UX) instead of intercepting it.
/// </summary>
public sealed class ActiveAppResolver
{
    // Confirmed via runtime logs: Explorer's DirectUIHWND requires 120-unit chunks.
    // Other classic Win32 scroll controls have the same accumulation behavior.
    private static readonly string[] _legacyScrollClasses =
    {
        "DirectUIHWND",  // Explorer folder view (runtime-confirmed)
        "SysListView32", // Classic Details view / common ListView
        "SysTreeView32", // Explorer nav pane / TreeView
        "ListBox",
        "LISTBOX",
    };

    private IntPtr  _cachedHwnd;
    private string? _cachedExeName;
    private string? _cachedParentExeName;
    private bool    _cachedIsElevated;
    private bool    _cachedIsLegacyScrollControl;

    /// <summary>
    /// Returns the exe filename, the parent process exe filename, whether the process is
    /// elevated, and whether the target HWND is a legacy Win32 scroll control.
    /// <para>
    /// <c>parentExeName</c> enables profile matching for sub-processes: e.g. if the window
    /// belongs to <c>steamwebhelper.exe</c> whose parent is <c>steam.exe</c>, a profile
    /// created for <c>steam.exe</c> will still apply.
    /// </para>
    /// </summary>
    public (string? exeName, string? parentExeName, bool isElevated, bool isLegacyScrollControl) TryGetWindowInfo(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return (null, null, false, false);

        if (hwnd == _cachedHwnd)
            return (_cachedExeName, _cachedParentExeName, _cachedIsElevated, _cachedIsLegacyScrollControl);

        _cachedHwnd = hwnd;
        QueryWindow(hwnd, out _cachedExeName, out _cachedParentExeName, out _cachedIsElevated, out _cachedIsLegacyScrollControl);
        return (_cachedExeName, _cachedParentExeName, _cachedIsElevated, _cachedIsLegacyScrollControl);
    }

    private static void QueryWindow(
        IntPtr hwnd,
        out string? exeName,
        out string? parentExeName,
        out bool isElevated,
        out bool isLegacyScrollControl)
    {
        exeName                = null;
        parentExeName          = null;
        isElevated             = false;
        isLegacyScrollControl  = false;

        // ── Child-class check ────────────────────────────────────────────────────────
        // WindowFromPoint returns the deepest child HWND at the cursor — exactly the
        // control that will receive WM_MOUSEWHEEL.  Check its class against the known
        // legacy list to decide whether chunked injection is needed.
        var classBuf = new StringBuilder(128);
        if (NativeMethods.GetClassName(hwnd, classBuf, classBuf.Capacity) > 0)
        {
            var cls = classBuf.ToString();
            foreach (var known in _legacyScrollClasses)
            {
                if (string.Equals(cls, known, StringComparison.OrdinalIgnoreCase))
                {
                    isLegacyScrollControl = true;
                    break;
                }
            }
        }

        // ── Process / elevation check ────────────────────────────────────────────────
        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return;

        var hFull = NativeMethods.OpenProcess(NativeMethods.ProcessQueryInformation, false, pid);
        if (hFull == IntPtr.Zero)
        {
            isElevated = true;
        }
        else
        {
            NativeMethods.CloseHandle(hFull);
        }

        var hLimited = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (hLimited == IntPtr.Zero) return;

        try
        {
            var sb  = new StringBuilder(1024);
            uint sz = (uint)sb.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(hLimited, 0, sb, ref sz)) return;
            exeName = Path.GetFileName(sb.ToString(0, (int)sz));
        }
        finally
        {
            NativeMethods.CloseHandle(hLimited);
        }

        parentExeName = GetParentExeName(pid);
    }

    /// <summary>
    /// Returns the exe filename of the parent process for <paramref name="pid"/>,
    /// or <c>null</c> if it cannot be determined.  Uses a process snapshot so it
    /// is only called once per unique HWND (cached in the caller).
    /// </summary>
    private static string? GetParentExeName(uint pid)
    {
        if (pid == 0) return null;

        var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshot == (IntPtr)(-1)) return null;

        try
        {
            var entry = new NativeMethods.PROCESSENTRY32
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>()
            };

            if (!NativeMethods.Process32First(snapshot, ref entry)) return null;

            uint parentPid = 0;
            do
            {
                if (entry.th32ProcessID == pid)
                {
                    parentPid = entry.th32ParentProcessID;
                    break;
                }
            } while (NativeMethods.Process32Next(snapshot, ref entry));

            if (parentPid == 0) return null;

            // Reset and find parent entry
            entry.dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32>();
            if (!NativeMethods.Process32First(snapshot, ref entry)) return null;
            do
            {
                if (entry.th32ProcessID == parentPid)
                    return entry.szExeFile;
            } while (NativeMethods.Process32Next(snapshot, ref entry));

            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }
    }
}
