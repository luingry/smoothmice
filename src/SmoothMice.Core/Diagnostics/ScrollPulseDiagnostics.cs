using System.Globalization;

namespace SmoothMice.Core.Diagnostics;

/// <summary>Raw physical wheel data captured before SmoothMice changes how scrolling is delivered.</summary>
public readonly record struct ScrollPulseDiagnosticPulse(
    DateTimeOffset Utc,
    long MonotonicTimestamp,
    bool IsHorizontal,
    short Delta,
    bool ShiftDown,
    int ScreenX,
    int ScreenY);

public readonly record struct ScrollPulseDiagnosticAnalysis(
    double? IntervalMilliseconds,
    int BurstCount120Milliseconds,
    bool IsShortBurst,
    bool IsRapidReversal);

/// <summary>A presentation-ready entry kept by the bounded live scroll monitor.</summary>
public readonly record struct ScrollPulseMonitorEntry(
    ScrollPulseDiagnosticPulse Pulse,
    ScrollPulseDiagnosticAnalysis Analysis,
    TimeSpan Elapsed);

/// <summary>
/// Keeps a resettable, bounded session of diagnostic results for an interactive monitor.
/// This deliberately has no UI or I/O dependency so its state transitions remain testable.
/// </summary>
public sealed class ScrollPulseMonitorSession
{
    public const int DefaultMaximumRows = 500;

    private readonly int _maximumRows;
    private readonly long _timestampFrequency;
    private readonly double _ticksPerMillisecond;
    private readonly List<ScrollPulseMonitorEntry> _entries = new();
    private ScrollPulseDiagnosticAnalyzer _analyzer;
    private long? _sessionStartTimestamp;

    public ScrollPulseMonitorSession(long timestampFrequency, int maximumRows = DefaultMaximumRows)
    {
        if (timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        if (maximumRows <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRows));

        _timestampFrequency = timestampFrequency;
        _ticksPerMillisecond = timestampFrequency / 1000d;
        _maximumRows = maximumRows;
        _analyzer = new ScrollPulseDiagnosticAnalyzer(timestampFrequency);
    }

    public IReadOnlyList<ScrollPulseMonitorEntry> Entries => _entries;
    public int TotalPulses { get; private set; }
    public int VerticalPulses { get; private set; }
    public int HorizontalPulses { get; private set; }
    public int ShortBursts { get; private set; }
    public int RapidReversals { get; private set; }
    public int DiscardedRows { get; private set; }

    public ScrollPulseMonitorEntry Record(ScrollPulseDiagnosticPulse pulse)
    {
        _sessionStartTimestamp ??= pulse.MonotonicTimestamp;
        var analysis = _analyzer.Analyze(pulse);
        var elapsed = TimeSpan.FromMilliseconds(
            (pulse.MonotonicTimestamp - _sessionStartTimestamp.Value) / _ticksPerMillisecond);
        var entry = new ScrollPulseMonitorEntry(pulse, analysis, elapsed);

        TotalPulses++;
        if (pulse.IsHorizontal)
            HorizontalPulses++;
        else
            VerticalPulses++;
        if (analysis.IsShortBurst)
            ShortBursts++;
        if (analysis.IsRapidReversal)
            RapidReversals++;

        if (_entries.Count == _maximumRows)
        {
            _entries.RemoveAt(0);
            DiscardedRows++;
        }
        _entries.Add(entry);
        return entry;
    }

    public void Clear()
    {
        _entries.Clear();
        _analyzer = new ScrollPulseDiagnosticAnalyzer(_timestampFrequency);
        _sessionStartTimestamp = null;
        TotalPulses = 0;
        VerticalPulses = 0;
        HorizontalPulses = 0;
        ShortBursts = 0;
        RapidReversals = 0;
        DiscardedRows = 0;
    }
}

/// <summary>
/// Applies presentation-only heuristics to raw pulses. These markers identify patterns worth
/// inspecting; they do not establish that a mouse has a hardware fault.
/// </summary>
public sealed class ScrollPulseDiagnosticAnalyzer
{
    public const double BurstWindowMilliseconds = 120;
    public const int BurstPulseThreshold = 4;
    public const double RapidReversalWindowMilliseconds = 150;

    private readonly AxisState _vertical = new();
    private readonly AxisState _horizontal = new();
    private readonly double _ticksPerMillisecond;

    public ScrollPulseDiagnosticAnalyzer(long timestampFrequency)
    {
        if (timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));

        _ticksPerMillisecond = timestampFrequency / 1000d;
    }

    public ScrollPulseDiagnosticAnalysis Analyze(ScrollPulseDiagnosticPulse pulse)
    {
        var state = pulse.IsHorizontal ? _horizontal : _vertical;
        double? interval = state.LastTimestamp is long previous
            ? (pulse.MonotonicTimestamp - previous) / _ticksPerMillisecond
            : null;

        while (state.RecentTimestamps.Count > 0 &&
               (pulse.MonotonicTimestamp - state.RecentTimestamps.Peek()) / _ticksPerMillisecond > BurstWindowMilliseconds)
        {
            state.RecentTimestamps.Dequeue();
        }

        state.RecentTimestamps.Enqueue(pulse.MonotonicTimestamp);
        var isRapidReversal = interval is <= RapidReversalWindowMilliseconds
            && state.LastDirection != 0
            && Math.Sign(pulse.Delta) != 0
            && Math.Sign(pulse.Delta) != state.LastDirection;

        state.LastTimestamp = pulse.MonotonicTimestamp;
        state.LastDirection = Math.Sign(pulse.Delta);

        var burstCount = state.RecentTimestamps.Count;
        return new ScrollPulseDiagnosticAnalysis(
            interval,
            burstCount,
            burstCount >= BurstPulseThreshold,
            isRapidReversal);
    }

    private sealed class AxisState
    {
        public long? LastTimestamp { get; set; }
        public int LastDirection { get; set; }
        public Queue<long> RecentTimestamps { get; } = new();
    }
}

public static class ScrollPulseDiagnosticFormatter
{
    /// <summary>Formats one NDJSON record so files can be read directly or parsed line-by-line.</summary>
    public static string FormatPulse(ScrollPulseDiagnosticPulse pulse, ScrollPulseDiagnosticAnalysis analysis)
    {
        var interval = analysis.IntervalMilliseconds is double value
            ? Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture)
            : "null";
        return string.Format(CultureInfo.InvariantCulture,
            "{{\"kind\":\"pulse\",\"utc\":\"{0:O}\",\"monotonic_ticks\":{1},\"axis\":\"{2}\",\"delta\":{3},\"direction\":\"{4}\",\"interval_ms\":{5},\"burst_120ms_count\":{6},\"short_burst\":{7},\"rapid_reversal\":{8},\"shift\":{9},\"x\":{10},\"y\":{11}}}",
            pulse.Utc,
            pulse.MonotonicTimestamp,
            pulse.IsHorizontal ? "horizontal" : "vertical",
            pulse.Delta,
            pulse.Delta > 0 ? "positive" : pulse.Delta < 0 ? "negative" : "zero",
            interval,
            analysis.BurstCount120Milliseconds,
            analysis.IsShortBurst.ToString().ToLowerInvariant(),
            analysis.IsRapidReversal.ToString().ToLowerInvariant(),
            pulse.ShiftDown.ToString().ToLowerInvariant(),
            pulse.ScreenX,
            pulse.ScreenY);
    }
}
