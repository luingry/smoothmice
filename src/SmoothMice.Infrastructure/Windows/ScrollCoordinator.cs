using SmoothMice.Core.Config;
using SmoothMice.Core.Profiles;
using SmoothMice.Core.Scrolling;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>Coordinates low-level hook, profile resolution, smoothing, and wheel injection.</summary>
public sealed class ScrollCoordinator : IDisposable
{
    private readonly ProfileManager _profiles;
    private readonly MouseHookService _hook;
    private readonly ScrollInjector _injector;
    private readonly ActiveAppResolver _apps;
    private readonly object _gate = new();

    private readonly SmoothScrollEngine _vertical   = new();
    private readonly SmoothScrollEngine _horizontal = new();

    private System.Threading.Timer? _tickTimer;
    private bool _running;
    private int _ticking;

    private double _smoothedIntervalMs = 400.0;
    private long   _lastEventMs        = -1;

    public ScrollCoordinator(
        ProfileManager profiles, MouseHookService hook,
        ScrollInjector injector, ActiveAppResolver apps)
    {
        _profiles = profiles;
        _hook     = hook;
        _injector = injector;
        _apps     = apps;
        _hook.MouseWheel += OnMouseWheel;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _hook.Install();
            _tickTimer ??= new System.Threading.Timer(_ => Tick(), null,
                Timeout.Infinite, Timeout.Infinite);
            _tickTimer.Change(4, 4);
            _running = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _tickTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _hook.Uninstall();
            _vertical.Reset();
            _horizontal.Reset();
            ResetEwma();
            _running = false;
        }
    }

    public void RefreshEnabledState()
    {
        if (!_profiles.Snapshot.Enabled) Stop();
        else Start();
    }

    private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
    {
        var snap = _profiles.Snapshot;
        if (!snap.Enabled) return;

        var exeName    = ActiveAppResolver.ExecutableNameFromPath(_apps.TryGetForegroundExecutablePath());
        var resolution = _profiles.ResolveForExecutable(exeName);
        if (!resolution.InterceptForSmoothing) return;

        var settings = resolution.Settings;

        if (e.IsHorizontal && !settings.HorizontalSmoothness) return;
        if (e.Delta == 0) return;

        var now   = Environment.TickCount64;
        double smoothed;
        lock (_gate) { smoothed = UpdateEwma(now, settings); }

        var accel = ScrollMath.AccelerationMultiplier(
            smoothed,
            settings.AccelerationDeltaMs,
            settings.AccelerationExponent,
            settings.AccelerationMaxX);

        e.Handled = true;

        lock (_gate)
        {
            if (e.IsHorizontal)
                _horizontal.PushPhysicalDelta(e.Delta, settings, accel);
            else
                _vertical.PushPhysicalDelta(e.Delta, settings, accel);
        }
    }

    private void Tick()
    {
        // Reentrancy guard: if the previous tick is still running (e.g. due to a slow Win32 call),
        // skip this invocation instead of letting threadpool callbacks pile up.
        if (System.Threading.Interlocked.CompareExchange(ref _ticking, 1, 0) != 0)
            return;
        try
        {
            TickCore();
        }
        finally
        {
            System.Threading.Volatile.Write(ref _ticking, 0);
        }
    }

    private void TickCore()
    {
        var snap = _profiles.Snapshot;
        if (!snap.Enabled) return;

        var exeName    = ActiveAppResolver.ExecutableNameFromPath(_apps.TryGetForegroundExecutablePath());
        var resolution = _profiles.ResolveForExecutable(exeName);
        if (!resolution.InterceptForSmoothing) return;

        var now      = Environment.TickCount64;
        var settings = resolution.Settings;

        int dv, dh;
        lock (_gate)
        {
            dv = _vertical.Tick(now, settings);
            dh = _horizontal.Tick(now, settings);
        }

        if (dv == 0 && dh == 0) return;

        if (!NativeMethods.GetCursorPos(out var pt)) return;
        var hwnd = NativeMethods.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return;

        var keys = 0u;
        if (NativeMethods.GetKeyState(NativeMethods.VkShift) < 0)
            keys |= NativeMethods.MkShift;

        if (dv != 0) _injector.TryPostWheel(hwnd, dv, horizontal: false, keys, pt);
        if (dh != 0) _injector.TryPostWheel(hwnd, dh, horizontal: true,  keys, pt);
    }

    /// <summary>EWMA-smoothed inter-event interval; call under <see cref="_gate"/>.</summary>
    private double UpdateEwma(long nowMs, ScrollProfileSettings settings)
    {
        const double alpha = 0.55;
        const double maxIntervalMs = 1200.0;
        const double resetPauseMs = 1500.0;

        if (_lastEventMs < 0 || nowMs - _lastEventMs >= resetPauseMs)
        {
            _smoothedIntervalMs = settings.AccelerationDeltaMs;
        }
        else
        {
            var actual = Math.Min(nowMs - _lastEventMs, maxIntervalMs);
            _smoothedIntervalMs = alpha * actual + (1.0 - alpha) * _smoothedIntervalMs;
        }

        _lastEventMs = nowMs;
        return _smoothedIntervalMs;
    }

    private void ResetEwma()
    {
        _smoothedIntervalMs = 400.0;
        _lastEventMs        = -1;
    }

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            _tickTimer?.Dispose();
            _tickTimer = null;
        }
        _hook.MouseWheel -= OnMouseWheel;
    }
}
