using System.Runtime.InteropServices;

namespace SmoothMice.Infrastructure.Windows;

public sealed class ScrollInjector
{
    private static readonly int _inputSize = Marshal.SizeOf<NativeMethods.INPUT>();

    /// <summary>
    /// Posts a wheel event directly to <paramref name="hwnd"/> via <c>PostMessage</c>.
    /// Used for non-elevated target windows.
    ///
    /// Delivers directly to the target's message queue, bypassing the OS
    /// "Scroll inactive windows" setting — guarantees the background window
    /// receives the scroll regardless of focus or OS configuration.
    ///
    /// <c>PostMessage</c> does NOT go through <c>WH_MOUSE_LL</c> (hardware-input only),
    /// so there is no re-processing loop.
    /// </summary>
    public bool TryPostWheel(
        IntPtr hwnd, int deltaUnits, bool horizontal,
        bool shiftDown, NativeMethods.POINT screenPt)
    {
        if (hwnd == IntPtr.Zero || deltaUnits == 0) return false;

        var msg       = horizontal ? (uint)NativeMethods.WmMousehwheel : (uint)NativeMethods.WmMousewheel;
        var modifiers = shiftDown ? NativeMethods.MkShift : 0u;
        var wParam    = (IntPtr)(((int)modifiers & 0xFFFF) | (deltaUnits << 16));
        var lParam    = unchecked((IntPtr)(uint)((ushort)(short)screenPt.X | ((uint)(ushort)(short)screenPt.Y << 16)));

        return NativeMethods.PostMessage(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Injects a wheel event via <c>SendInput</c>.
    /// Used for elevated-process windows (Task Manager, regedit, …) where
    /// <c>PostMessage</c> would be silently dropped by UIPI.
    ///
    /// <c>SendInput</c> operates at the hardware-input level and bypasses UIPI
    /// entirely — the elevated window receives the smooth scroll correctly.
    /// The injected event carries <c>LLMHF_INJECTED</c> so our hook skips it.
    /// </summary>
    public bool TryInjectWheel(int deltaUnits, bool horizontal)
    {
        if (deltaUnits == 0) return false;

        var input = new NativeMethods.INPUT
        {
            Type = 0, // INPUT_MOUSE
            Mi   = new NativeMethods.MOUSEINPUT
            {
                Flags     = horizontal ? NativeMethods.MOUSEEVENTF_HWHEEL : NativeMethods.MOUSEEVENTF_WHEEL,
                MouseData = (uint)deltaUnits,
            }
        };

        return NativeMethods.SendInput(1, new[] { input }, _inputSize) == 1;
    }
}
