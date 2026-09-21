using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using SmoothMice.Core.Diagnostics;
using SmoothMice.Core.Scrolling;

namespace SmoothMice.App.ViewModels;

/// <summary>UI-facing, bounded presentation state for the live physical scroll monitor.</summary>
public sealed class ScrollPulseMonitorViewModel : ViewModelBase
{
    private readonly ScrollPulseMonitorSession _session = new(Stopwatch.Frequency);
    private int _ingressDropped;
    private int _ticksScheduled;
    private int _ticksSkippedReentrancy;

    public ObservableCollection<ScrollPulseMonitorRow> Rows { get; } = new();

    public bool HasRows => Rows.Count != 0;
    public bool EmptyStateVisible => !HasRows;
    public string StatusText
    {
        get
        {
            if (!HasRows)
                return "Turn the wheel to start.";

            var discarded = _session.DiscardedRows + _ingressDropped;
            return discarded == 0
                ? "Capturing physical pulses live. Keeping the latest 500."
                : $"Capturing live. {discarded:N0} old or excess pulses were discarded.";
        }
    }

    public string TotalText => $"{_session.TotalPulses:N0} pulses";
    public string AxisText => $"{_session.VerticalPulses:N0} vertical · {_session.HorizontalPulses:N0} horizontal";
    // ShortBursts counts marked pulses, not distinct burst episodes.
    public string BurstText => $"{_session.ShortBursts:N0} marked";
    public string ReversalText => $"{_session.RapidReversals:N0} reversals";

    /// <summary>
    /// Diagnostic for "does the scroll drop pulses even though the logger sees them all": the
    /// 4 ms tick timer skips a scheduled callback outright (not merely emits a small delta) when
    /// the previous tick is still busy with a slow SendInput/PostMessage call — see
    /// ScrollCoordinator.Tick's reentrancy guard. A skip rate that tracks scroll activity here
    /// points at that guard rather than at the smoothing math itself.
    /// </summary>
    public string TickDiagnosticsText => _ticksScheduled == 0
        ? "No animation ticks yet."
        : $"{_ticksScheduled:N0} ticks scheduled, {_ticksSkippedReentrancy:N0} skipped (previous tick still running)";

    /// <param name="stepSizePx">
    /// The active global profile's StepSizePx, used only to show the base (pre-acceleration)
    /// pixel equivalent of each raw pulse for diagnostics — not the actual injected amount,
    /// which also depends on acceleration and the per-tick animation.
    /// </param>
    public void Add(ScrollPulseDiagnosticPulse pulse, double stepSizePx)
    {
        var entry = _session.Record(pulse);
        if (Rows.Count == ScrollPulseMonitorSession.DefaultMaximumRows)
            Rows.RemoveAt(0);
        Rows.Add(new ScrollPulseMonitorRow(entry, stepSizePx));
        RaiseSummary();
    }

    public void SetIngressDropped(int value)
    {
        if (_ingressDropped == value)
            return;
        _ingressDropped = value;
        Raise(nameof(StatusText));
    }

    public void SetTickDiagnostics(int ticksScheduled, int ticksSkippedReentrancy)
    {
        if (_ticksScheduled == ticksScheduled && _ticksSkippedReentrancy == ticksSkippedReentrancy)
            return;
        _ticksScheduled = ticksScheduled;
        _ticksSkippedReentrancy = ticksSkippedReentrancy;
        Raise(nameof(TickDiagnosticsText));
    }

    public void Clear()
    {
        _session.Clear();
        _ingressDropped = 0;
        Rows.Clear();
        RaiseSummary();
    }

    private void RaiseSummary()
    {
        Raise(nameof(HasRows));
        Raise(nameof(EmptyStateVisible));
        Raise(nameof(StatusText));
        Raise(nameof(TotalText));
        Raise(nameof(AxisText));
        Raise(nameof(BurstText));
        Raise(nameof(ReversalText));
    }
}

public sealed class ScrollPulseMonitorRow
{
    public ScrollPulseMonitorRow(ScrollPulseMonitorEntry entry, double stepSizePx)
    {
        var pulse = entry.Pulse;
        var analysis = entry.Analysis;
        RelativeTime = $"+{entry.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)} s";
        UtcTime = pulse.Utc.ToUniversalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " UTC";
        Axis = pulse.IsHorizontal ? "Horizontal" : "Vertical";
        DeltaAndDirection = FormatDelta(pulse);
        PixelDelta = FormatPixelDelta(pulse, stepSizePx);
        Interval = analysis.IntervalMilliseconds is double interval
            ? $"{interval.ToString("0.#", CultureInfo.InvariantCulture)} ms"
            : "—";
        BurstWindow = $"{analysis.BurstCount120Milliseconds}/120 ms";
        Markers = FormatMarkers(analysis);
        IsAnomalous = analysis.IsShortBurst || analysis.IsRapidReversal;
    }

    public string RelativeTime { get; }
    public string UtcTime { get; }
    public string Axis { get; }
    public string DeltaAndDirection { get; }
    public string PixelDelta { get; }
    public string Interval { get; }
    public string BurstWindow { get; }
    public string Markers { get; }
    public bool IsAnomalous { get; }

    // WPF's default ItemAutomationPeer name (read by Narrator/UI Automation tooltips) falls
    // back to object.ToString() when nothing else is set. The GridView only wires up
    // DisplayMemberBinding per-column, so without this override that fallback showed the bare
    // type name instead of the row's actual data.
    public override string ToString() =>
        $"{RelativeTime} | {UtcTime} | {Axis} | {DeltaAndDirection} | {PixelDelta} | interval {Interval} | window {BurstWindow}" +
        (string.IsNullOrEmpty(Markers) ? "" : $" | {Markers}");

    private static string FormatDelta(ScrollPulseDiagnosticPulse pulse)
    {
        var direction = pulse.IsHorizontal
            ? pulse.Delta >= 0 ? "→" : "←"
            : pulse.Delta >= 0 ? "↑" : "↓";
        return $"{pulse.Delta:+#;-#;0} {direction}";
    }

    // Base (pre-acceleration) pixel equivalent of this raw pulse, using the same StepScale the
    // smoothing engine applies (see SmoothScrollEngine.PushPhysicalDelta). This is NOT the actual
    // injected amount — that also depends on the live acceleration multiplier and gets spread
    // across many animation ticks — but it is the stable, per-event figure useful for comparing
    // whether the *raw* pulse itself is bigger on some events (e.g. hardware/driver behavior)
    // versus a discrepancy introduced by the smoothing/acceleration layer.
    private static string FormatPixelDelta(ScrollPulseDiagnosticPulse pulse, double stepSizePx)
    {
        var px = pulse.Delta * ScrollMath.StepScale(stepSizePx);
        return $"{px.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture)} px";
    }

    private static string FormatMarkers(ScrollPulseDiagnosticAnalysis analysis)
    {
        if (analysis.IsShortBurst && analysis.IsRapidReversal)
            return "Burst · Reversal";
        if (analysis.IsShortBurst)
            return "Burst";
        return analysis.IsRapidReversal ? "Reversal" : "";
    }
}
