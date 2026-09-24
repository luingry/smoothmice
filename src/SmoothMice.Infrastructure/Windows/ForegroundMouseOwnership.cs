using Microsoft.Win32;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Keeps the wheel inside a fullscreen game whose hidden cursor has drifted onto another monitor.
/// </summary>
/// <remarks>
/// Games such as Dying Light: The Beast hide the pointer, read raw input and neither capture nor
/// clip the cursor, so the invisible system cursor wanders onto a second monitor. With the Windows
/// default "scroll inactive windows when hovering" routing, Windows itself then delivers every
/// wheel notch to the app under that invisible cursor (the game still gets it via raw input).
/// A low-level hook cannot re-route a single event, so while that situation holds the system
/// wheel routing is switched to "focused window" at runtime (never persisted) and restored as
/// soon as it no longer holds.
/// </remarks>
public sealed class ForegroundMouseOwnership
{
    private const uint RoutingFocus = 0;
    private const string DesktopKey = @"Control Panel\Desktop";

    private readonly object _sync = new();
    private uint? _originalRouting;

    public ForegroundMouseOwnership()
    {
        // Crash recovery: the runtime switch is never written to the registry, so the persisted
        // value is the user's real preference. Re-apply it in case a previous run died switched.
        var persisted = ReadPersistedRouting();
        if (persisted is { } value && TryGetRouting(out var current) && current != value)
            _ = SetRouting(value);
    }

    /// <summary>
    /// Pure decision: the wheel belongs to the foreground window when the pointer is hidden, the
    /// foreground fills its monitor, and the window under the (invisible) cursor is another one.
    /// </summary>
    public static bool ShouldRouteToForeground(
        bool targetIsForegroundRoot,
        bool cursorIsHidden,
        bool foregroundIsFullscreen) =>
        !targetIsForegroundRoot && cursorIsHidden && foregroundIsFullscreen;

    /// <summary>Win32 probe for <see cref="ShouldRouteToForeground(bool,bool,bool)"/>.</summary>
    public static bool ShouldRouteToForeground(IntPtr hwndUnderCursor)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        var targetRoot = hwndUnderCursor == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.GetAncestor(hwndUnderCursor, NativeMethods.GaRoot);

        return ShouldRouteToForeground(
            targetRoot == foreground,
            IsCursorHidden(),
            IsFullscreenOnItsMonitor(foreground));
    }

    /// <summary>
    /// Switches wheel routing to the focused window. Returns true only when this call changed it,
    /// i.e. the current event was already routed by the old setting and must be re-injected.
    /// </summary>
    public bool EnsureFocusRouting()
    {
        lock (_sync)
        {
            if (_originalRouting is not null)
                return false;
            if (!TryGetRouting(out var current) || current == RoutingFocus)
                return false;
            if (!SetRouting(RoutingFocus))
                return false;

            _originalRouting = current;
            return true;
        }
    }

    /// <summary>Restores the routing that was active before <see cref="EnsureFocusRouting"/>.</summary>
    public void Restore()
    {
        lock (_sync)
        {
            if (_originalRouting is not { } original)
                return;
            _ = SetRouting(original);
            _originalRouting = null;
        }
    }

    private static bool TryGetRouting(out uint routing)
    {
        routing = 0;
        return NativeMethods.SystemParametersInfo(
            NativeMethods.SpiGetMouseWheelRouting, 0, ref routing, 0);
    }

    // No SPIF_UPDATEINIFILE: runtime only, so a reboot always restores the user's setting.
    private static bool SetRouting(uint routing) =>
        NativeMethods.SystemParametersInfo(
            NativeMethods.SpiSetMouseWheelRouting, 0, new IntPtr(routing), 0);

    private static uint? ReadPersistedRouting()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DesktopKey);
            return key?.GetValue("MouseWheelRouting") is int value ? (uint)value : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsCursorHidden()
    {
        var info = new NativeMethods.CURSORINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.CURSORINFO>(),
        };
        return NativeMethods.GetCursorInfo(ref info) && (info.flags & NativeMethods.CursorShowing) == 0;
    }

    private static bool IsFullscreenOnItsMonitor(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return false;

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;

        var m = info.rcMonitor;
        return rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
    }
}
