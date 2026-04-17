using SmoothMice.Core.Config;
using SmoothMice.Core.Profiles;
using SmoothMice.Core.Scrolling;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Brings together hook, profiles, smoothing engines and injection.
///
/// Acceleration is computed from an exponentially-weighted moving average
/// (EWMA) of inter-event intervals, feeding a continuous power-curve
/// multiplier — no discrete speed tiers, no stepping artefacts.
/// </summary>
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

    // ── EWMA acceleration state ───────────────────────────────────────────
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

    // ── Hook callback ─────────────────────────────────────────────────────

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

    // ── Tick (animation timer) ────────────────────────────────────────────

    private void Tick()
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

    // ── EWMA helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Updates and returns the EWMA-smoothed inter-event interval.
    /// Must be called under <see cref="_gate"/>.
    ///
    /// Alpha = 0.55 → ramps from slow to fast in ~3 events, smooth enough
    /// to avoid stepping, responsive enough to feel the acceleration change.
    ///
    /// After a pause ≥ 1.5 s the EWMA is reset to the reference interval
    /// so the very first event in a new gesture starts at neutral speed.
    /// </summary>
    private double UpdateEwma(long nowMs, ScrollProfileSettings settings)
    {
        const double alpha          = 0.55;
        const double maxIntervalMs  = 1200.0;  // cap very slow intervals
        const double resetPauseMs   = 1500.0;  // treat as new gesture after pause

        if (_lastEventMs < 0 || nowMs - _lastEventMs >= resetPauseMs)
        {
            // New session: start neutral so there's no sudden jump.
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

    // ── IDisposable ───────────────────────────────────────────────────────

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
