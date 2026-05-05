namespace SmoothMice.Infrastructure.Windows;

public sealed class ScrollInjector
{
    /// <summary>
    /// Posts a wheel event directly to <paramref name="hwnd"/> via <c>PostMessage</c>.
    ///
    /// Using <c>PostMessage</c> with the hook-time HWND (from <c>WindowFromPoint</c> in
    /// <c>OnMouseWheel</c>) guarantees delivery to the exact target window regardless of:
    ///  • OS "Scroll inactive windows" setting — <c>SendInput</c> obeys that setting and
    ///    routes to the focused window when it is OFF; <c>PostMessage</c> bypasses it.
    ///  • Current keyboard focus — the message goes to the hwnd's message queue directly.
    ///
    /// <c>PostMessage</c> does NOT go through the <c>WH_MOUSE_LL</c> hook (which only
    /// intercepts raw hardware input), so there is no re-processing loop — no need for
    /// the LLMHF_INJECTED filter when using this path.
    ///
    /// <paramref name="screenPt"/>: the screen-coordinate position captured at hook time;
    /// packed into lParam exactly as Windows does for real WM_MOUSEWHEEL events.
    /// </summary>
    public bool TryPostWheel(
        IntPtr hwnd, int deltaUnits, bool horizontal,
        bool shiftDown, NativeMethods.POINT screenPt)
    {
        if (hwnd == IntPtr.Zero || deltaUnits == 0) return false;

        var msg      = horizontal ? (uint)NativeMethods.WmMousehwheel : (uint)NativeMethods.WmMousewheel;
        var modifiers = shiftDown ? NativeMethods.MkShift : 0u;
        var wParam   = (IntPtr)(((int)modifiers & 0xFFFF) | (deltaUnits << 16));
        var lParam   = unchecked((IntPtr)(uint)((ushort)(short)screenPt.X | ((uint)(ushort)(short)screenPt.Y << 16)));

        return NativeMethods.PostMessage(hwnd, msg, wParam, lParam);
    }
}
