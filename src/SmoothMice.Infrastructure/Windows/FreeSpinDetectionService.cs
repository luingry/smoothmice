using System.Diagnostics;
using SmoothMice.Core.Diagnostics;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Free-Spin inertia suppression on the hook thread. Every wheel pulse is decided by
/// <see cref="FreeSpinWobblePolicy"/> the moment it arrives; nothing is held or delayed. Any error
/// is pass-through.
/// </summary>
public sealed class FreeSpinDetectionService : IDisposable
{
    private readonly MouseHookService _hook;
    private readonly FreeSpinWobblePolicy _wobble = new();
    private readonly object _historyGate = new();
    private FreeSpinRuntimeSettings _settings = new(false, FreeSpinDetectionMode.ReadOnly);
    private bool _physicalSubscribed;
    private bool _disposed;
    private FreeSpinDecisionLogger? _decisionLog;
    private readonly string? _decisionLogDirectory;

    public FreeSpinDetectionService(MouseHookService hook, string? decisionLogDirectory = null)
    {
        _hook = hook ?? throw new ArgumentNullException(nameof(hook));
        _decisionLogDirectory = decisionLogDirectory;
        // Subscribe before ScrollCoordinator is constructed so Suppress can stop an original wheel
        // before it reaches smoothing/injection. It stays fail-open while the module is disabled.
        _hook.MouseWheel += OnWheel;
    }

    public event EventHandler<FreeSpinDetectionDecision>? DecisionObserved;

    public string Summary =>
        $"Drops a first pulse {_wobble.SuspectMinimumMoveAgeMs:F0}–{_wobble.SuspectMoveAgeMs:F0} ms after a sharp movement (≥{_wobble.SuspectMinimumEndSpeed:F0} px/s), " +
        $"a reversed pulse within {FreeSpinWobblePolicy.ReboundMs:F0} ms, and slow rocking after it. Scrolling while moving or after a gentle stop always passes. No pulse is ever delayed.";

    /// <summary>Path of the live decision log, or null when it is not recording.</summary>
    public string? DecisionLogPath => Volatile.Read(ref _decisionLog)?.Path;

    public void Configure(bool enabled, FreeSpinDetectionMode mode, bool logDecisions)
    {
        var previous = Volatile.Read(ref _settings);
        Volatile.Write(ref _settings, new FreeSpinRuntimeSettings(enabled, mode));
        if (previous.Enabled != enabled || previous.Mode != mode)
        {
            // A new policy must never inherit wheel/move context from the old policy.
            lock (_historyGate) _wobble.Reset();
        }
        // XY/button objects are not produced by MouseHookService unless a live feature consumer is active.
        // This subscription switch happens off the callback and cannot make a physical event block.
        if (enabled && !_physicalSubscribed) { _hook.PhysicalInputCaptured += OnPhysicalInput; _physicalSubscribed = true; }
        else if (!enabled && _physicalSubscribed) { _hook.PhysicalInputCaptured -= OnPhysicalInput; _physicalSubscribed = false; }

        var record = enabled && logDecisions;
        if (record && Volatile.Read(ref _decisionLog) is null)
            Volatile.Write(ref _decisionLog, FreeSpinDecisionLogger.TryStartDefault(_decisionLogDirectory));
        else if (!record)
            Interlocked.Exchange(ref _decisionLog, null)?.Dispose();
    }

    private void OnPhysicalInput(object? sender, FreeSpinPhysicalInputEventArgs e)
    {
        if (Volatile.Read(ref _settings).Enabled == false || e.Input.Kind == FreeSpinRawEventKind.Wheel) return;
        try
        {
            lock (_historyGate)
            {
                var input = e.Input;
                if (input.Kind == FreeSpinRawEventKind.Move) _wobble.ObserveMove(input.StopwatchTicks, input.X, input.Y);
                else if (input.Kind == FreeSpinRawEventKind.Button && input.Button != FreeSpinMouseButton.None)
                    _wobble.ObserveButton(1 << ((int)input.Button - 1), input.IsButtonDown);
            }
        }
        catch { }
    }

    private void OnWheel(object? sender, MouseWheelHookEventArgs e)
    {
        var settings = Volatile.Read(ref _settings);
        if (!settings.Enabled) return;
        FreeSpinDetectionDecision decision;
        double movementPx = 0;
        double? moveAgeMs = null;
        try
        {
            FreeSpinWobbleDecision wobble;
            lock (_historyGate)
            {
                var ticks = Stopwatch.GetTimestamp();
                (movementPx, moveAgeMs) = _wobble.Movement(ticks);
                wobble = _wobble.OnWheel(ticks, e.Delta, e.IsHorizontal);
            }
            var drop = wobble.Action == FreeSpinWobbleAction.Suppress;
            var swallow = settings.Mode == FreeSpinDetectionMode.Suppress && drop;
            if (swallow) e.Handled = true;
            decision = new FreeSpinDetectionDecision
            {
                Verdict = drop ? FreeSpinVerdict.InertiaCandidate : FreeSpinVerdict.Pass,
                Eligible = drop,
                ShouldSuppress = swallow,
                Reason = (drop ? "[drop] " : "[pass] ") + wobble.Reason,
            };
        }
        catch { decision = new FreeSpinDetectionDecision { Verdict = FreeSpinVerdict.Abstain, Reason = "Detector error; event passed." }; }
        Volatile.Read(ref _decisionLog)?.Record(e.Delta, e.IsHorizontal, movementPx, moveAgeMs, decision);
        try { DecisionObserved?.Invoke(this, decision); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hook.MouseWheel -= OnWheel;
        if (_physicalSubscribed) _hook.PhysicalInputCaptured -= OnPhysicalInput;
        Interlocked.Exchange(ref _decisionLog, null)?.Dispose();
    }

    private sealed class FreeSpinRuntimeSettings
    {
        public FreeSpinRuntimeSettings(bool enabled, FreeSpinDetectionMode mode) { Enabled = enabled; Mode = mode; }
        public bool Enabled { get; }
        public FreeSpinDetectionMode Mode { get; }
    }
}
