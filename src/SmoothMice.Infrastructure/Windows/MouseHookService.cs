using System.Runtime.InteropServices;

namespace SmoothMice.Infrastructure.Windows;

public sealed class MouseHookService : IDisposable
{
    private readonly object _sync = new();
    private IntPtr _hook = IntPtr.Zero;
    private NativeMethods.LowLevelMouseProc? _proc;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public event EventHandler<MouseWheelHookEventArgs>? MouseWheel;

    public void Install()
    {
        lock (_sync)
        {
            if (_hook != IntPtr.Zero)
                return;

            _proc = HookCallback;
            var module = NativeMethods.GetModuleHandle(null);
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _proc, module, 0);
            if (_hook == IntPtr.Zero)
                throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Uninstall()
    {
        lock (_sync)
        {
            if (_hook == IntPtr.Zero)
                return;
            _ = NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _proc = null;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && MouseWheel is not null)
        {
            var msg = wParam.ToInt32();
            if (msg is NativeMethods.WmMousewheel or NativeMethods.WmMousehwheel)
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var delta = unchecked((short)(unchecked((uint)info.mouseData) >> 16));
                var horizontal = msg == NativeMethods.WmMousehwheel;
                var shift = (NativeMethods.GetKeyState(NativeMethods.VkShift) & 0x8000) != 0;
                var args = new MouseWheelHookEventArgs(delta, horizontal, shift, info.pt);
                MouseWheel.Invoke(this, args);
                if (args.Handled)
                    return (IntPtr)1;
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}

public sealed class MouseWheelHookEventArgs : EventArgs
{
    public MouseWheelHookEventArgs(short delta, bool horizontal, bool shiftDown, NativeMethods.POINT screenPoint)
    {
        Delta = delta;
        IsHorizontal = horizontal;
        IsShiftDown = shiftDown;
        ScreenPoint = screenPoint;
    }

    public short Delta { get; }
    public bool IsHorizontal { get; }
    public bool IsShiftDown { get; }
    public NativeMethods.POINT ScreenPoint { get; }

    /// <summary>When true, the original wheel message is swallowed.</summary>
    public bool Handled { get; set; }
}
