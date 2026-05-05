using SmoothMice.Core.Config;
using SmoothMice.Core.Profiles;
using SmoothMice.Core.Scrolling;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>Coordinates low-level hook, profile resolution, smoothing, and wheel injection.</summary>
///
/// <remarks>
/// Performance design — zero idle overhead:
///
///   The tick timer and timeBeginPeriod(1) are ONLY active while there is motion to animate.
///
///   • <c>OnMouseWheel</c>: arms the timer (and requests 1 ms scheduler period) on the
///     first event after an idle period.  Caches <see cref="ScrollProfileSettings"/> so
///     <c>TickCore</c> never calls <c>GetForegroundWindow</c>, <c>ResolveForExecutable</c>,
///     or <c>ProfileManager.Snapshot</c> — those are expensive per-call and have no place
///     in a 4 ms hot loop.
///
///   • <c>TickCore</c>: uses the cached settings; after the tick, if both engines report
///     IsQuiet it disarms the timer and releases the 1 ms period.
///
///   Net result during idle: 0 Win32 calls/second, default system timer resolution restored.
///   Net result while scrolling: 1 ms period + 4 ms tick, exactly as needed for smooth animation.
/// </remarks>
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
    private int  _ticking;
    private bool _timerPeriodSet;

    // Cached profile settings for the current scroll session.
    // Written in OnMouseWheel (hook thread) under _gate.
    // Read in TickCore (threadpool) under _gate.
    // Nulled out when the animation goes quiet.
    private ScrollProfileSettings? _cachedSettings;

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

            // Timer starts STOPPED.  It is armed in OnMouseWheel the moment a wheel
            // event arrives, and disarmed again when both engines become quiet.
            // This means the 4 ms tick and the 1 ms scheduler period are only
            // active while there is motion to animate — zero idle overhead.
            _tickTimer ??= new System.Threading.Timer(_ => Tick(), null,
                Timeout.Infinite, Timeout.Infinite);

            _running = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            DisarmTimer();              // stops tick + releases 1ms period
            _hook.Uninstall();
            _vertical.Reset();
            _horizontal.Reset();
            ResetEwma();
            _cachedSettings = null;
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

        // Resolve against the window UNDER THE CURSOR, not the foreground window.
        // Scroll events are delivered to the window under the cursor regardless of
        // keyboard focus — using GetForegroundWindow() here would pick the wrong
        // profile when the user scrolls a background window.
        var hwndTarget = NativeMethods.WindowFromPoint(e.ScreenPoint);
        var exeName    = ActiveAppResolver.ExecutableNameFromPath(_apps.TryGetExecutablePathForHwnd(hwndTarget));
        var resolution = _profiles.ResolveForExecutable(exeName);
        if (!resolution.InterceptForSmoothing) return;

        var settings = resolution.Settings;

        if (e.IsHorizontal && !settings.HorizontalSmoothness) return;
        if (e.Delta == 0) return;

        // When Ctrl is physically held, let the event pass through unchanged so that
        // Ctrl+scroll (zoom in Explorer/browsers, volume adjust, etc.) works natively.
        if (NativeMethods.GetKeyState(NativeMethods.VkControl) < 0) return;

        var now = EnvironmentEx.TickCount64;

        lock (_gate)
        {
            var wasQuiet = _vertical.IsQuiet() && _horizontal.IsQuiet();

            UpdateEwma(now, settings);

            var accel = ScrollMath.AccelerationMultiplier(
                _smoothedIntervalMs,
                settings.AccelerationDeltaMs,
                settings.AccelerationExponent,
                settings.AccelerationMaxX);

            e.Handled = true;

            // Cache settings so TickCore never has to call GetForegroundWindow /
            // ResolveForExecutable / ProfileManager.Snapshot during the tick loop.
            _cachedSettings = settings;

            if (e.IsHorizontal)
                _horizontal.PushPhysicalDelta(e.Delta, settings, accel, now);
            else
                _vertical.PushPhysicalDelta(e.Delta, settings, accel, now);

            // Arm the timer only when transitioning from idle to active.
            // If already animating the timer is already running — no-op needed.
            if (wasQuiet)
                ArmTimer();
        }
    }

    private void Tick()
    {
        // Reentrancy guard: if the previous tick is still running (e.g. due to a slow
        // SendInput call), skip this invocation instead of letting callbacks pile up.
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
        ScrollProfileSettings? settings;
        int dv, dh;

        lock (_gate)
        {
            settings = _cachedSettings;
            if (settings == null) return;

            var now = EnvironmentEx.TickCount64;
            dv = _vertical.Tick(now, settings);
            dh = _horizontal.Tick(now, settings);

            // Once both engines are quiet, disarm the timer and release the 1ms
            // scheduler period so the system returns to its default resolution.
            if (_vertical.IsQuiet() && _horizontal.IsQuiet())
            {
                DisarmTimer();
                _cachedSettings = null;
            }
        }

        // Injection happens outside the lock: SendInput is a kernel call and
        // holding _gate during it would block OnMouseWheel on the hook thread.
        if (dv != 0) _injector.TryInjectWheel(dv, horizontal: false);
        if (dh != 0) _injector.TryInjectWheel(dh, horizontal: true);
    }

    // ── Timer arm / disarm — must be called under _gate ────────────────────

    private void ArmTimer()
    {
        if (!_timerPeriodSet)
        {
            // Request 1 ms system timer resolution so the 4 ms tick fires at ~4 ms.
            // Only active while we are animating; released in DisarmTimer().
            NativeMethods.timeBeginPeriod(1);
            _timerPeriodSet = true;
        }
        _tickTimer?.Change(4, 4);
    }

    private void DisarmTimer()
    {
        _tickTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        if (_timerPeriodSet)
        {
            NativeMethods.timeEndPeriod(1);
            _timerPeriodSet = false;
        }
    }

    // ── EWMA — must be called under _gate ───────────────────────────────────

    private void UpdateEwma(long nowMs, ScrollProfileSettings settings)
    {
        const double alpha         = 0.55;
        const double maxIntervalMs = 1200.0;
        const double resetPauseMs  = 1500.0;

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
