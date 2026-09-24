using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// A single delivery thread waiting on a high-resolution, one-shot Windows timer. No spinning,
/// ThreadPool timer quantization, overlapping callbacks or periodic wakeups while idle.
/// </summary>
internal sealed class ScrollTickScheduler : IDisposable
{
    public const int IntervalMs = 4;
    private readonly object _gate = new();
    private readonly Action _tick;
    private readonly AutoResetEvent _changed = new(false);
    private readonly TimerHandle _timer;
    private readonly Thread _thread;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _active, _disposed, _periodSet;
    private long _generation;

    public bool IsHighResolution { get; }
    public Task Completion => _completion.Task;

    internal ScrollTickScheduler(Action tick, bool forceFallback = false)
    {
        _tick = tick;
        var handle = forceFallback ? IntPtr.Zero : CreateWaitableTimerExW(IntPtr.Zero, null, 2, 0x100002);
        IsHighResolution = handle != IntPtr.Zero;
        if (handle == IntPtr.Zero)
            handle = CreateWaitableTimerExW(IntPtr.Zero, null, 0, 0x100002);
        if (handle == IntPtr.Zero)
        {
            _changed.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        _timer = new TimerHandle(handle);
        _thread = new Thread(Run) { IsBackground = true, Name = "SmoothMice scroll delivery" };
        _thread.Start();
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ScrollTickScheduler));
            if (_active) return;
            if (!IsHighResolution)
                _periodSet = NativeMethods.timeBeginPeriod(1) == 0;
            _active = true;
            _generation++;
            Arm(IntervalMs);
            _changed.Set();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed) return;
            StopCore();
            _changed.Set();
        }
    }

    private void StopCore()
    {
        _active = false;
        _generation++;
        CancelWaitableTimer(_timer.SafeWaitHandle);
        if (_periodSet)
        {
            NativeMethods.timeEndPeriod(1);
            _periodSet = false;
        }
    }

    private void Arm(double milliseconds)
    {
        long due = -(long)Math.Ceiling(milliseconds * 10_000);
        if (!SetWaitableTimer(_timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void Run()
    {
        try
        {
            var waits = new WaitHandle[] { _changed, _timer };
            while (true)
            {
                var signaled = WaitHandle.WaitAny(waits);
                long generation;
                lock (_gate)
                {
                    if (_disposed) return;
                    if (signaled == 0 || !_active) continue;
                    generation = _generation;
                }
                var started = Stopwatch.GetTimestamp();
                _tick(); // Never hold the scheduler lock across a callback or native input.
                var spent = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                lock (_gate)
                {
                    if (_disposed) return;
                    if (_active && generation == _generation)
                        Arm(spent < IntervalMs ? Math.Max(1, IntervalMs - spent) : IntervalMs);
                }
            }
        }
        catch (Exception ex)
        {
            // An exception must not terminate the process from a background thread.
            Trace.TraceError("Scroll scheduler stopped: {0}", ex);
            _completion.TrySetException(ex);
        }
        finally
        {
            lock (_gate)
            {
                StopCore();
                _disposed = true;
                _timer.Dispose();
                _changed.Dispose();
            }
            _completion.TrySetResult(true);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            StopCore();
            _disposed = true;
            _changed.Set();
        }
        // Do not Join on the hook/UI thread: an in-flight SendInput may be waiting for it.
        // The worker closes its handles in finally. Completion is available for diagnostics/tests.
    }

    private sealed class TimerHandle : WaitHandle
    {
        public TimerHandle(IntPtr handle) => SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period, IntPtr callback, IntPtr state, bool resume);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelWaitableTimer(SafeWaitHandle timer);
}
