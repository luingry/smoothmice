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
    private readonly object _gameCacheGate = new();
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

    // Game classification is deliberately a separate cache. It is queried only when the global
    // opt-in is on, and never from the coordinator's 4 ms animation tick.
    private GameWindowCacheEntry _cachedGame;

    /// <summary>
    /// Lists the visible top-level windows that can still be resolved to an executable.
    /// A process or window can disappear during enumeration, so individual probe failures are
    /// intentionally ignored instead of making the picker unusable.
    /// </summary>
    public static IReadOnlyList<RunningApplicationWindow> EnumerateVisibleTopLevelWindows()
    {
        var windows = new List<RunningApplicationWindow>();
        NativeMethods.EnumWindowsProc callback = (hwnd, lParam) =>
        {
            try
            {
                if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd))
                    return true;

                _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
                if (processId == 0)
                    return true;

                var executablePath = GetExecutablePath(processId);
                if (string.IsNullOrWhiteSpace(executablePath))
                    return true;

                var executableName = Path.GetFileName(executablePath);
                if (string.IsNullOrWhiteSpace(executableName))
                    return true;

                windows.Add(new RunningApplicationWindow(
                    hwnd,
                    processId,
                    executableName,
                    GetWindowTitle(hwnd)));
            }
            catch
            {
                // Access can be denied and windows can close while EnumWindows is running.
            }

            return true;
        };

        try
        {
            _ = NativeMethods.EnumWindows(callback, IntPtr.Zero);
        }
        catch
        {
            // Enumeration is optional UI affordance; fail closed to an empty result.
        }

        return windows
            .OrderBy(window => window.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Returns true only for a conservatively identified game window. Win32 query failures and
    /// unknown classes return false so callers preserve the current native/smoothing behavior.
    /// </summary>
    public bool IsLikelyGameWindow(IntPtr hwnd) =>
        IsLikelyGameWindow(hwnd, out _, out _);

    /// <summary>
    /// Classifies a target from root-window signals and returns the root process identity plus
    /// executable name when the probe succeeds. Callers can reuse that executable when resolving
    /// the same process, avoiding a second <c>QueryFullProcessImageName</c> in the hook path.
    /// </summary>
    public bool IsLikelyGameWindow(IntPtr hwnd, out uint processId, out string? executableName)
    {
        processId = 0;
        executableName = null;
        if (hwnd == IntPtr.Zero)
            return false;

        IntPtr rootHwnd;
        try
        {
            rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
            if (rootHwnd == IntPtr.Zero || !NativeMethods.IsWindow(rootHwnd))
                return false;

            _ = NativeMethods.GetWindowThreadProcessId(rootHwnd, out processId);
            if (processId == 0)
                return false;
        }
        catch
        {
            return false;
        }

        var nowMs = EnvironmentEx.TickCount64;
        GameWindowCacheEntry cached;
        lock (_gameCacheGate)
            cached = _cachedGame;

        if (!GameWindowCachePolicy.CanReuse(
                cached.RootHwnd, cached.ProcessId, cached.IsComplete, cached.CachedAtMs,
                rootHwnd, processId, currentRootIsWindow: true, nowMs))
        {
            cached = QueryGameRoot(rootHwnd, processId, nowMs);
            lock (_gameCacheGate)
                _cachedGame = cached;
        }

        if (!cached.IsComplete)
            return false;

        executableName = cached.ExecutableName;
        return GameWindowClassifier.IsLikelyGame(
            targetClassName: null,
            cached.RootClassName,
            executableName,
            rootHwnd == NativeMethods.GetForegroundWindow(),
            cached.RootIsFullscreenOrBorderless);
    }

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
        => TryGetWindowInfo(hwnd, knownProcessId: 0, knownExecutableName: null);

    /// <summary>
    /// Resolves normal profile information. When the game probe already read the executable for
    /// this same process, pass it here to avoid duplicating the process-image query.
    /// </summary>
    public (string? exeName, string? parentExeName, bool isElevated, bool isLegacyScrollControl) TryGetWindowInfo(
        IntPtr hwnd, uint knownProcessId, string? knownExecutableName)
    {
        if (hwnd == IntPtr.Zero)
            return (null, null, false, false);

        if (hwnd == _cachedHwnd)
            return (_cachedExeName, _cachedParentExeName, _cachedIsElevated, _cachedIsLegacyScrollControl);

        _cachedHwnd = hwnd;
        QueryWindow(
            hwnd, knownProcessId, knownExecutableName,
            out _cachedExeName, out _cachedParentExeName, out _cachedIsElevated, out _cachedIsLegacyScrollControl);
        return (_cachedExeName, _cachedParentExeName, _cachedIsElevated, _cachedIsLegacyScrollControl);
    }

    private static void QueryWindow(
        IntPtr hwnd, uint knownProcessId, string? knownExecutableName,
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

        exeName = pid == knownProcessId && !string.IsNullOrWhiteSpace(knownExecutableName)
            ? knownExecutableName
            : GetExecutableName(pid);
        if (exeName is null) return;

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

    private static GameWindowCacheEntry QueryGameRoot(IntPtr rootHwnd, uint processId, long nowMs)
    {
        try
        {
            var rootClassName = GetWindowClassName(rootHwnd);
            var executableName = GetExecutableName(processId);
            if (rootClassName is null || executableName is null)
                return new GameWindowCacheEntry(rootHwnd, processId, nowMs, isComplete: false, null, null, false);

            return new GameWindowCacheEntry(
                rootHwnd,
                processId,
                nowMs,
                isComplete: true,
                rootClassName,
                executableName,
                IsFullscreenOrBorderless(rootHwnd));
        }
        catch
        {
            // Game detection must never interfere with physical input on a Win32 failure.
            return new GameWindowCacheEntry(rootHwnd, processId, nowMs, isComplete: false, null, null, false);
        }
    }

    private static string? GetWindowClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }

    private static string? GetExecutableName(uint pid)
    {
        var executablePath = GetExecutablePath(pid);
        return executablePath is null ? null : Path.GetFileName(executablePath);
    }

    private static string? GetExecutablePath(uint pid)
    {
        if (pid == 0)
            return null;

        var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
            return null;

        try
        {
            var path = new StringBuilder(32768);
            uint length = (uint)path.Capacity;
            return NativeMethods.QueryFullProcessImageName(process, 0, path, ref length)
                ? path.ToString(0, (int)length)
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
            return string.Empty;

        var title = new StringBuilder(length + 1);
        return NativeMethods.GetWindowText(hwnd, title, title.Capacity) > 0
            ? title.ToString()
            : string.Empty;
    }

    private static bool IsFullscreenOrBorderless(IntPtr rootHwnd)
    {
        if (!NativeMethods.GetWindowRect(rootHwnd, out var windowRect))
            return false;

        var monitor = NativeMethods.MonitorFromWindow(rootHwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;

        if (Covers(windowRect, info.rcMonitor))
            return true;

        var style = unchecked((uint)NativeMethods.GetWindowStyle(rootHwnd).ToInt64());
        var hasStandardFrame = (style & (NativeMethods.WsCaption | NativeMethods.WsThickFrame)) != 0;
        return !hasStandardFrame && Covers(windowRect, info.rcWork);
    }

    private static bool Covers(NativeMethods.RECT window, NativeMethods.RECT bounds)
    {
        const int tolerancePx = 2;
        return window.Left <= bounds.Left + tolerancePx &&
               window.Top <= bounds.Top + tolerancePx &&
               window.Right >= bounds.Right - tolerancePx &&
               window.Bottom >= bounds.Bottom - tolerancePx;
    }

    private readonly struct GameWindowCacheEntry
    {
        public GameWindowCacheEntry(
            IntPtr rootHwnd, uint processId, long cachedAtMs, bool isComplete,
            string? rootClassName, string? executableName, bool rootIsFullscreenOrBorderless)
        {
            RootHwnd = rootHwnd;
            ProcessId = processId;
            CachedAtMs = cachedAtMs;
            IsComplete = isComplete;
            RootClassName = rootClassName;
            ExecutableName = executableName;
            RootIsFullscreenOrBorderless = rootIsFullscreenOrBorderless;
        }

        public IntPtr RootHwnd { get; }
        public uint ProcessId { get; }
        public long CachedAtMs { get; }
        public bool IsComplete { get; }
        public string? RootClassName { get; }
        public string? ExecutableName { get; }
        public bool RootIsFullscreenOrBorderless { get; }
    }
}
