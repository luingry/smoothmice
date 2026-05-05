using System.Runtime.CompilerServices;
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

            // Pre-JIT the delegate and its call chain before handing the pointer to Windows.
            // Without this, the very first mouse event triggers JIT compilation inside the
            // hook callback — the hook must return before Windows continues delivering input,
            // so any JIT delay is felt as a direct mouse stutter.
            // PrepareDelegate also ensures the GC never moves the stub while it is live in
            // native code (the delegate itself is already kept alive by _proc, but the
            // reverse-pinvoke thunk benefits from the explicit preparation).
            RuntimeHelpers.PrepareDelegate(_proc);

            // WH_MOUSE_LL / WH_KEYBOARD_LL: Windows Vista+ requires hMod == NULL (global low-level hook).
            // A non-null module handle (e.g. apphost from GetModuleHandle(null)) can make SetWindowsHookEx fail,
            // which crashes startup right after install (single-file publish).
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _proc, IntPtr.Zero, 0);
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

                // Skip events we injected ourselves via SendInput — prevents re-processing
                // our own smoothed events and potentially double-smoothing them.
                if ((info.flags & NativeMethods.LlmhfInjected) != 0)
                    return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
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
