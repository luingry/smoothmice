using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using SmoothMice.Core.Diagnostics;

namespace SmoothMice.Infrastructure.Windows;

public sealed class MouseHookService : IDisposable
{
    private readonly object _sync = new();
    private readonly ScrollPulseLogger? _scrollPulseLogger;
    private IntPtr _hook = IntPtr.Zero;
    private NativeMethods.LowLevelMouseProc? _proc;

    public bool IsInstalled => _hook != IntPtr.Zero;

    internal WheelInjectionGuard? InjectionGuard { get; set; }

    public event EventHandler<MouseWheelHookEventArgs>? MouseWheel;

    /// <summary>
    /// Raw physical wheel pulses, emitted before smoothing. Subscribers must return immediately:
    /// this event runs on the low-level hook callback.
    /// </summary>
    public event EventHandler<ScrollPulseCapturedEventArgs>? ScrollPulseCaptured;

    /// <summary>
    /// Physical mouse events for an active calibration recorder. This is diagnostic-only: the
    /// hook never waits for it and subscriber faults are isolated from normal mouse input.
    /// </summary>
    public event EventHandler<FreeSpinPhysicalInputEventArgs>? PhysicalInputCaptured;

    public MouseHookService(ScrollPulseLogger? scrollPulseLogger = null) =>
        _scrollPulseLogger = scrollPulseLogger;

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
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            var needsWheel = MouseWheel is not null || _scrollPulseLogger is not null || ScrollPulseCaptured is not null || InjectionGuard is not null;
            var needsPhysical = PhysicalInputCaptured is not null;
            var isWheel = msg is NativeMethods.WmMousewheel or NativeMethods.WmMousehwheel;
            if ((needsWheel && isWheel) || (needsPhysical && IsPhysicalMessage(msg)))
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

                // SendInput may have started before a physical reversal was handled. Reject
                // stale own output here, at actual hook delivery, not only before SendInput.
                if (isWheel && (info.flags & NativeMethods.LlmhfInjected) != 0 &&
                    InjectionGuard?.ShouldSuppress(info.dwExtraInfo, msg == NativeMethods.WmMousehwheel) == true)
                    return (IntPtr)1;

                // Skip events we injected ourselves via SendInput — prevents re-processing
                // our own smoothed events and potentially double-smoothing them.
                if (ShouldIgnoreInjected(info.flags, info.dwExtraInfo))
                    return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                if (needsPhysical)
                    PublishPhysicalInput(CreatePhysicalInput(msg, info));

                if (needsWheel && isWheel)
                {
                    var delta = unchecked((short)(unchecked((uint)info.mouseData) >> 16));
                    var horizontal = msg == NativeMethods.WmMousehwheel;
                    var shift = (NativeMethods.GetKeyState(NativeMethods.VkShift) & 0x8000) != 0;
                    if (_scrollPulseLogger is not null || ScrollPulseCaptured is not null)
                    {
                        var pulse = new ScrollPulseDiagnosticPulse(
                            DateTimeOffset.UtcNow,
                            Stopwatch.GetTimestamp(),
                            horizontal,
                            delta,
                            shift,
                            info.pt.X,
                            info.pt.Y);
                        _scrollPulseLogger?.Record(pulse);
                        PublishCapturedPulse(pulse);
                    }
                    var args = new MouseWheelHookEventArgs(delta, horizontal, shift, info.pt);
                    MouseWheel?.Invoke(this, args);
                    if (args.Handled)
                        return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// dwExtraInfo tag ("WINPUT") that Winput LAN stamps on the input it replays from a remote
    /// machine. That input is a real physical wheel on the other side, so it is smoothed like local
    /// hardware; our own SendInput carries no tag and stays ignored.
    /// </summary>
    public const long WinputLanInputTag = 0x57494E505554;

    // Windows hands WH_MOUSE_LL only the low 32 bits of dwExtraInfo (0x4E505554 here), even to 64-bit hooks.
    private const long ExtraInfoHookMask = 0xFFFFFFFF;

    public static bool ShouldIgnoreInjected(uint flags, IntPtr extraInfo) =>
        (flags & NativeMethods.LlmhfInjected) != 0 &&
        (extraInfo.ToInt64() & ExtraInfoHookMask) != (WinputLanInputTag & ExtraInfoHookMask);

    private static bool IsPhysicalMessage(int msg) => msg is
        NativeMethods.WmMousemove or NativeMethods.WmMousewheel or NativeMethods.WmMousehwheel or
        NativeMethods.WmLbuttondown or NativeMethods.WmLbuttonup or NativeMethods.WmRbuttondown or
        NativeMethods.WmRbuttonup or NativeMethods.WmMbuttondown or NativeMethods.WmMbuttonup or
        NativeMethods.WmXbuttondown or NativeMethods.WmXbuttonup;

    private static FreeSpinRawInputEvent CreatePhysicalInput(int msg, NativeMethods.MSLLHOOKSTRUCT info)
    {
        var input = new FreeSpinRawInputEvent
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            StopwatchTicks = Stopwatch.GetTimestamp(),
            X = info.pt.X,
            Y = info.pt.Y,
        };
        if (msg is NativeMethods.WmMousewheel or NativeMethods.WmMousehwheel)
        {
            input.Kind = FreeSpinRawEventKind.Wheel;
            input.WheelDelta = unchecked((short)(unchecked((uint)info.mouseData) >> 16));
            input.WheelAxis = msg == NativeMethods.WmMousehwheel ? "horizontal" : "vertical";
            return input;
        }
        if (msg == NativeMethods.WmMousemove)
        {
            input.Kind = FreeSpinRawEventKind.Move;
            return input;
        }
        input.Kind = FreeSpinRawEventKind.Button;
        input.IsButtonDown = msg is NativeMethods.WmLbuttondown or NativeMethods.WmRbuttondown or NativeMethods.WmMbuttondown or NativeMethods.WmXbuttondown;
        input.Button = msg is NativeMethods.WmLbuttondown or NativeMethods.WmLbuttonup ? FreeSpinMouseButton.Left :
            msg is NativeMethods.WmRbuttondown or NativeMethods.WmRbuttonup ? FreeSpinMouseButton.Right :
            msg is NativeMethods.WmMbuttondown or NativeMethods.WmMbuttonup ? FreeSpinMouseButton.Middle :
            ((unchecked((uint)info.mouseData) >> 16) & 0xffff) == 1 ? FreeSpinMouseButton.X1 : FreeSpinMouseButton.X2;
        return input;
    }

    private void PublishPhysicalInput(FreeSpinRawInputEvent input)
    {
        var subscribers = PhysicalInputCaptured;
        if (subscribers is null)
            return;
        var args = new FreeSpinPhysicalInputEventArgs(input);
        try { subscribers(this, args); }
        catch { /* diagnostics are always fail-open */ }
    }

    private void PublishCapturedPulse(ScrollPulseDiagnosticPulse pulse)
    {
        var subscribers = ScrollPulseCaptured;
        if (subscribers is null)
            return;

        var args = new ScrollPulseCapturedEventArgs(pulse);
        foreach (EventHandler<ScrollPulseCapturedEventArgs> subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber(this, args);
            }
            catch
            {
                // Diagnostics must stay transparent even if a live subscriber fails.
            }
        }
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

public sealed class ScrollPulseCapturedEventArgs : EventArgs
{
    public ScrollPulseCapturedEventArgs(ScrollPulseDiagnosticPulse pulse) => Pulse = pulse;

    public ScrollPulseDiagnosticPulse Pulse { get; }
}

public sealed class FreeSpinPhysicalInputEventArgs : EventArgs
{
    public FreeSpinPhysicalInputEventArgs(FreeSpinRawInputEvent input) => Input = input;
    public FreeSpinRawInputEvent Input { get; }
}
