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
    private readonly ForegroundMouseOwnership _mouseOwnership = new();

    private readonly SmoothScrollEngine _vertical   = new();
    private readonly SmoothScrollEngine _horizontal = new();

    private System.Threading.Timer? _tickTimer;
    private bool _running;
    private int  _ticking;
    private bool _timerPeriodSet;
    private long _sessionGeneration;

    // Diagnostics only — read by the Scroll logs window to help tell apart "the engine math
    // emitted a small delta" from "a scheduled tick never ran at all" (e.g. the reentrancy guard
    // below skipping it because the previous tick was still busy with a slow SendInput/PostMessage
    // call). Incremented on the timer thread, read from the UI thread — Interlocked/Volatile only.
    private int _diagTicksScheduled;
    private int _diagTicksSkippedReentrancy;

    public (int scheduled, int skippedReentrancy) GetTickDiagnostics() =>
        (System.Threading.Volatile.Read(ref _diagTicksScheduled),
         System.Threading.Volatile.Read(ref _diagTicksSkippedReentrancy));

    // Cached state for the current scroll session — written in OnMouseWheel under _gate,
    // read in TickCore under _gate, cleared when engines go quiet.
    //
    //  _cachedHwnd       : window under cursor at hook time (WindowFromPoint).
    //  _cachedScreenPt   : cursor screen-coords, packed into PostMessage lParam.
    //  _cachedSettings   : resolved ScrollProfileSettings for this session.
    //  _cachedIsElevated : whether the target process requires SendInput (UIPI).
    //
    // Injection strategy — re-evaluated on EVERY tick in TickCore:
    //
    //   • FOCUSED window  →  SendInput(MOUSEEVENTF_WHEEL)
    //       The target owns the keyboard focus, so SendInput routes to it correctly.
    //       Sends authentic hardware input: OS generates WM_MOUSEWHEEL AND WM_POINTERWHEEL
    //       (the modern pointer-input path).  This is essential for apps like the Windows 11
    //       Task Manager (WinUI 3), modern shell controls (DirectUIHWND), etc.
    //
    //   • BACKGROUND window  →  PostMessage(hwnd, WM_MOUSEWHEEL)
    //       Delivers directly to the target HWND's message queue, bypassing the OS
    //       "Scroll inactive windows when I hover over them" routing.  The background
    //       window scrolls regardless of focus or OS setting.
    //       PostMessage never enters WH_MOUSE_LL (hardware-input only), so no loop.
    //
    //   • ELEVATED process override  →  always SendInput
    //       PostMessage to a High-integrity process is silently dropped by UIPI.
    //
    // Focus is re-checked on each tick (not cached) so that opening the settings window
    // or switching focus mid-animation never routes remaining events to the wrong target.
    private IntPtr                 _cachedHwnd;
    private NativeMethods.POINT    _cachedScreenPt;
    private ScrollProfileSettings? _cachedSettings;
    private bool                   _cachedIsElevated;

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
        _profiles.SettingsChanged += OnSettingsChanged;
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
            _mouseOwnership.Restore();
            _vertical.Reset();
            _horizontal.Reset();
            ResetEwma();
            _cachedSettings   = null;
            _cachedHwnd       = IntPtr.Zero;
            _cachedIsElevated = false;
            _sessionGeneration++;
            _running = false;
        }
    }

    public void RefreshEnabledState() => Start();

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // Enabling the global bypass must also stop an already queued animation for a game,
        // even if no additional wheel event arrives. ActiveAppResolver serializes this cached
        // out-of-tick query with the hook thread.
        if (!_profiles.DoNotActivateInGames)
            return;

        IntPtr target;
        lock (_gate)
            target = _cachedHwnd;

        if (target != IntPtr.Zero && _apps.IsLikelyGameWindow(target))
            CancelPendingAnimationFor(target);
    }

    private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
    {
        // MouseWheel is multicast. The Free-Spin detector subscribes before this coordinator
        // and may already have consumed a high-confidence physical event. Do not resolve a
        // target, queue motion, or inject a replacement after that decision.
        if (e.Handled)
            return;

        // Resolve against the window UNDER THE CURSOR (not the foreground window).
        var hwndTarget = NativeMethods.WindowFromPoint(e.ScreenPoint);

        // A fullscreen game hid the pointer and it drifted onto another monitor: the window under
        // it is not what the user is scrolling, and Windows' hover routing would scroll it anyway.
        // Route the wheel to the focused game instead and leave the event unsmoothed.
        if (ForegroundMouseOwnership.ShouldRouteToForeground(hwndTarget))
        {
            if (_mouseOwnership.EnsureFocusRouting())
            {
                // This event was already routed under the old setting — swallow it and replay it
                // off the hook thread so the new focus routing (and the game's raw input) gets it.
                e.Handled = true;
                var delta = e.Delta;
                var horizontal = e.IsHorizontal;
                System.Threading.ThreadPool.QueueUserWorkItem(
                    _ => _injector.TryInjectWheel(delta, horizontal));
            }
            return;
        }
        _mouseOwnership.Restore();

        // This opt-in is evaluated before profile resolution, interception, or e.Handled. A
        // conservative positive leaves the original physical event entirely to Windows/the game.
        // Detection is cached by ActiveAppResolver and never runs in the animation tick.
        uint gameProcessId = 0;
        string? gameExecutableName = null;
        if (_profiles.DoNotActivateInGames &&
            _apps.IsLikelyGameWindow(hwndTarget, out gameProcessId, out gameExecutableName))
        {
            CancelPendingAnimationFor(hwndTarget);
            return;
        }

        var (exeName, parentExeName, isElevated, isLegacyScrollControl) =
            _apps.TryGetWindowInfo(hwndTarget, gameProcessId, gameExecutableName);
        var resolution = _profiles.ResolveForExecutable(exeName, parentExeName);
        if (!resolution.InterceptForSmoothing) return;

        var settings = resolution.Settings;

        if (e.IsHorizontal && !settings.HorizontalSmoothness) return;
        if (e.Delta == 0) return;

        // When Ctrl is physically held, let the event pass through unchanged so that
        // Ctrl+scroll (zoom in Explorer/browsers, volume adjust, etc.) works natively.
        if (NativeMethods.GetKeyState(NativeMethods.VkControl) < 0) return;

        // Legacy Win32 scroll controls (DirectUIHWND, SysListView32, …) only respond to
        // full WHEEL_DELTA (120-unit) inputs and produce a "stall then jump" effect with
        // our sub-120 smooth injections.  Pass through unchanged for native scroll UX.
        if (isLegacyScrollControl) return;

        var now = EnvironmentEx.TickCount64;

        lock (_gate)
        {
            PrepareTarget(hwndTarget);
            var wasQuiet = _vertical.IsQuiet() && _horizontal.IsQuiet();

            UpdateEwma(now, settings, wasQuiet);

            var accel = ScrollMath.AccelerationMultiplier(
                _smoothedIntervalMs,
                settings.AccelerationDeltaMs,
                settings.AccelerationExponent,
                settings.AccelerationMaxX);

            e.Handled = true;

            // Cache the target HWND and elevation status for the animation ticks.
            // Focus (focused vs. background) is NOT cached here — it is re-evaluated on
            // every tick in TickCore so that opening the settings window or switching focus
            // mid-animation never routes the remaining events to the wrong window.
            _cachedHwnd       = hwndTarget;
            _cachedScreenPt   = e.ScreenPoint;
            _cachedSettings   = settings;
            _cachedIsElevated = isElevated;

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

    // Called under _gate before adding a pulse. Pending motion belongs to one exact control,
    // including child HWNDs within the same app; it must never migrate to another destination.
    private void PrepareTarget(IntPtr target)
    {
        if (_cachedHwnd != IntPtr.Zero && _cachedHwnd != target)
        {
            _vertical.Reset();
            _horizontal.Reset();
            ResetEwma();
            _sessionGeneration++;
        }
    }

    private void Tick()
    {
        System.Threading.Interlocked.Increment(ref _diagTicksScheduled);

        // Reentrancy guard: if the previous tick is still running (e.g. due to a slow
        // SendInput call), skip this invocation instead of letting callbacks pile up.
        if (System.Threading.Interlocked.CompareExchange(ref _ticking, 1, 0) != 0)
        {
            System.Threading.Interlocked.Increment(ref _diagTicksSkippedReentrancy);
            return;
        }
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
        IntPtr              hwnd;
        NativeMethods.POINT screenPt;
        bool                isElevated;
        long                sessionGeneration;
        int dv, dh;

        lock (_gate)
        {
            settings = _cachedSettings;
            if (settings == null) return;

            hwnd       = _cachedHwnd;
            screenPt   = _cachedScreenPt;
            isElevated = _cachedIsElevated;
            sessionGeneration = _sessionGeneration;

            var now = EnvironmentEx.TickCount64;
            dv = _vertical.Tick(now);
            dh = _horizontal.Tick(now);

            if (_vertical.IsQuiet() && _horizontal.IsQuiet())
            {
                DisarmTimer();
                _cachedSettings = null;
                _cachedHwnd     = IntPtr.Zero;
            }
        }

        // Re-evaluate focus on every tick (not cached from scroll start).
        // This prevents injecting into a stale window if the user opens the settings
        // window or alt-tabs during an ongoing animation.
        var rootOfTarget = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
        var foreground   = NativeMethods.GetForegroundWindow();
        bool targetFocused = rootOfTarget != IntPtr.Zero && rootOfTarget == foreground;
        bool useSendInput  = targetFocused || isElevated;

        // SendInput is routed by Windows to the window under the CURRENT cursor. If the cursor
        // has since left the target (e.g. a game's hidden cursor drifting onto another monitor),
        // SendInput would scroll that other window. Post to the cached target instead, or drop
        // the delta when UIPI makes posting impossible.
        if (useSendInput && !CursorIsOverRoot(rootOfTarget))
        {
            if (isElevated) return;
            useSendInput = false;
        }

        // Do not hold _gate across SendInput/PostMessage. In the foreground path this is
        // SendInput, and a FreeSpin burst can otherwise make WH_MOUSE_LL wait behind a native
        // injection on every 4 ms tick. Cancellation invalidates the session and clears both
        // engines; at most this already-calculated, bounded tick can race with the cancellation.
        if (sessionGeneration != System.Threading.Volatile.Read(ref _sessionGeneration))
            return;

        if (useSendInput)
        {
            if (dv != 0) _injector.TryInjectWheel(dv, horizontal: false);
            if (dh != 0) _injector.TryInjectWheel(dh, horizontal: true);
        }
        else
        {
            var shiftDown = NativeMethods.GetKeyState(NativeMethods.VkShift) < 0;
            if (dv != 0) _injector.TryPostWheel(hwnd, dv, horizontal: false, shiftDown, screenPt);
            if (dh != 0) _injector.TryPostWheel(hwnd, dh, horizontal: true,  shiftDown, screenPt);
        }
    }

    private static bool CursorIsOverRoot(IntPtr root)
    {
        if (root == IntPtr.Zero || !NativeMethods.GetCursorPos(out var pt))
            return false;

        var under = NativeMethods.WindowFromPoint(pt);
        return under != IntPtr.Zero && NativeMethods.GetAncestor(under, NativeMethods.GaRoot) == root;
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

    private void UpdateEwma(long nowMs, ScrollProfileSettings settings, bool wasQuiet)
    {
        _smoothedIntervalMs = ScrollMath.UpdateSmoothedInterval(
            _smoothedIntervalMs, _lastEventMs, nowMs, wasQuiet, settings.AccelerationDeltaMs);
        _lastEventMs = nowMs;
    }

    private void ResetEwma()
    {
        _smoothedIntervalMs = 400.0;
        _lastEventMs        = -1;
    }

    /// <summary>
    /// Drops pending motion for a newly bypassed game target. The generation change also makes a
    /// concurrent tick that already consumed deltas abandon them before it can inject.
    /// </summary>
    private void CancelPendingAnimationFor(IntPtr hwnd)
    {
        lock (_gate)
        {
            if (_cachedHwnd == IntPtr.Zero)
                return;

            // WindowFromPoint can resolve a different child HWND on consecutive events while
            // both children belong to the same game root. Cancel that whole destination too.
            var cachedRoot = NativeMethods.GetAncestor(_cachedHwnd, NativeMethods.GaRoot);
            var bypassedRoot = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
            if (_cachedHwnd != hwnd &&
                (cachedRoot == IntPtr.Zero || bypassedRoot == IntPtr.Zero || cachedRoot != bypassedRoot))
                return;

            _vertical.Reset();
            _horizontal.Reset();
            DisarmTimer();
            _cachedSettings = null;
            _cachedHwnd = IntPtr.Zero;
            _cachedIsElevated = false;
            ResetEwma();
            _sessionGeneration++;
        }
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
        _profiles.SettingsChanged -= OnSettingsChanged;
    }
}
