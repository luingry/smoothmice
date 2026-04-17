namespace SmoothMice.Infrastructure.Windows;

public sealed class ScrollInjector
{
    public bool TryPostWheel(IntPtr targetHwnd, int deltaUnits, bool horizontal, nuint keys, NativeMethods.POINT screenPt)
    {
        if (targetHwnd == IntPtr.Zero || deltaUnits == 0)
            return false;

        var msg = horizontal ? (uint)NativeMethods.WmMousehwheel : (uint)NativeMethods.WmMousewheel;
        var wParam = (IntPtr)(((int)keys & 0xFFFF) | (deltaUnits << 16));
        var lp = unchecked((IntPtr)(uint)((ushort)(short)screenPt.X | ((uint)(ushort)(short)screenPt.Y << 16)));
        return NativeMethods.PostMessage(targetHwnd, msg, wParam, lp);
    }
}
