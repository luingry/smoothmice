using System.Runtime.InteropServices;

namespace SmoothMice.Infrastructure.Windows;

public sealed class ScrollInjector
{
    private static readonly int _inputSize = Marshal.SizeOf<NativeMethods.INPUT>();

    /// <summary>
    /// Injects a wheel event via <c>SendInput</c>.
    ///
    /// Using <c>SendInput</c> (instead of the previous <c>PostMessage</c>) means:
    ///  • The event travels through the normal OS input path and is delivered to
    ///    whichever window is under the cursor, including system overlays such as
    ///    the Windows 11 Snap Layout panel.
    ///  • Key modifiers (Ctrl, Shift) are read from the actual keyboard state by the
    ///    receiving application — Ctrl+scroll zoom, Ctrl+scroll volume, etc. all work
    ///    correctly without having to embed modifier flags in the message.
    ///  • Our hook skips events flagged as <c>LLMHF_INJECTED</c>, preventing any
    ///    re-processing loop.
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
                MouseData = (uint)deltaUnits, // signed int re-interpreted as DWORD; receivers see it as signed
            }
        };

        return NativeMethods.SendInput(1, new[] { input }, _inputSize) == 1;
    }
}
