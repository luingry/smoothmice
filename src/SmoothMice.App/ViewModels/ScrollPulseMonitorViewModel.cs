using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using SmoothMice.Core.Diagnostics;

namespace SmoothMice.App.ViewModels;

/// <summary>UI-facing, bounded presentation state for the live physical scroll monitor.</summary>
public sealed class ScrollPulseMonitorViewModel : ViewModelBase
{
    private readonly ScrollPulseMonitorSession _session = new(Stopwatch.Frequency);
    private int _ingressDropped;

    public ObservableCollection<ScrollPulseMonitorRow> Rows { get; } = new();

    public bool HasRows => Rows.Count != 0;
    public bool EmptyStateVisible => !HasRows;
    public string StatusText
    {
        get
        {
            if (!HasRows)
                return "Gire a roda para começar.";

            var discarded = _session.DiscardedRows + _ingressDropped;
            return discarded == 0
                ? "Capturando pulsos físicos em tempo real. Mantendo até 500 dos mais recentes."
                : $"Capturando em tempo real. {discarded:N0} pulsos antigos ou em excesso foram descartados.";
        }
    }

    public string TotalText => $"{_session.TotalPulses:N0} pulsos";
    public string AxisText => $"{_session.VerticalPulses:N0} vertical · {_session.HorizontalPulses:N0} horizontal";
    // ShortBursts counts marked pulses, not distinct burst episodes.
    public string BurstText => $"{_session.ShortBursts:N0} marcados";
    public string ReversalText => $"{_session.RapidReversals:N0} reversões";

    public void Add(ScrollPulseDiagnosticPulse pulse)
    {
        var entry = _session.Record(pulse);
        if (Rows.Count == ScrollPulseMonitorSession.DefaultMaximumRows)
            Rows.RemoveAt(0);
        Rows.Add(new ScrollPulseMonitorRow(entry));
        RaiseSummary();
    }

    public void SetIngressDropped(int value)
    {
        if (_ingressDropped == value)
            return;
        _ingressDropped = value;
        Raise(nameof(StatusText));
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
    public ScrollPulseMonitorRow(ScrollPulseMonitorEntry entry)
    {
        var pulse = entry.Pulse;
        var analysis = entry.Analysis;
        RelativeTime = $"+{entry.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)} s";
        UtcTime = pulse.Utc.ToUniversalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " UTC";
        Axis = pulse.IsHorizontal ? "Horizontal" : "Vertical";
        DeltaAndDirection = FormatDelta(pulse);
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
    public string Interval { get; }
    public string BurstWindow { get; }
    public string Markers { get; }
    public bool IsAnomalous { get; }

    private static string FormatDelta(ScrollPulseDiagnosticPulse pulse)
    {
        var direction = pulse.IsHorizontal
            ? pulse.Delta >= 0 ? "→" : "←"
            : pulse.Delta >= 0 ? "↑" : "↓";
        return $"{pulse.Delta:+#;-#;0} {direction}";
    }

    private static string FormatMarkers(ScrollPulseDiagnosticAnalysis analysis)
    {
        if (analysis.IsShortBurst && analysis.IsRapidReversal)
            return "Rajada · Reversão";
        if (analysis.IsShortBurst)
            return "Rajada";
        return analysis.IsRapidReversal ? "Reversão" : "";
    }
}
