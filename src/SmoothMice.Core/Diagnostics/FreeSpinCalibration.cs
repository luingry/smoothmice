using System.Diagnostics;

namespace SmoothMice.Core.Diagnostics;

/// <summary>Distinct physical situations collected before any free-spin heuristic exists.</summary>
public enum FreeSpinCalibrationPhase
{
    Lift,
    Landing,
    Reposition,
    LegitimateScroll,
}

public enum FreeSpinRawEventKind
{
    Move,
    Button,
    Wheel,
}

public enum FreeSpinMouseButton
{
    None,
    Left,
    Right,
    Middle,
    X1,
    X2,
}

/// <summary>Small immutable-at-ingress representation of a physical hook event.</summary>
public sealed class FreeSpinRawInputEvent
{
    public FreeSpinRawEventKind Kind { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public long StopwatchTicks { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int WheelDelta { get; set; }
    public string? WheelAxis { get; set; }
    public FreeSpinMouseButton Button { get; set; }
    public bool IsButtonDown { get; set; }
}

/// <summary>A schema-v1 event stored within one calibration gesture.</summary>
public sealed class FreeSpinCalibrationEvent
{
    public string Kind { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; }
    public long StopwatchTicks { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Dx { get; set; }
    public int Dy { get; set; }
    public int WheelDelta { get; set; }
    public string? WheelAxis { get; set; }
    public int ButtonMask { get; set; }
    public string? ButtonTransition { get; set; }
}

/// <summary>Portable, versioned unit persisted for a completed calibration gesture.</summary>
public sealed class FreeSpinCalibrationSample
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    public long StartedStopwatchTicks { get; set; }
    public long CompletedStopwatchTicks { get; set; }
    public int TargetSnapshot { get; set; }
    public int DroppedEventCount { get; set; }
    public List<FreeSpinCalibrationEvent> Events { get; set; } = [];
}

/// <summary>
/// Bounded in-memory builder. It is deliberately independent from hooks and file I/O so the
/// hot callback only gives it already captured physical events.
/// </summary>
public sealed class FreeSpinCalibrationSession
{
    public const int DefaultMaximumEvents = 4_096;
    public static readonly long MinimumDurationStopwatchTicks = Stopwatch.Frequency / 2;
    private readonly List<FreeSpinCalibrationEvent> _events = [];
    private readonly int _maximumEvents;
    private int _buttonMask;
    private bool _hasPreviousPosition;
    private int _previousX;
    private int _previousY;

    public FreeSpinCalibrationSession(
        FreeSpinCalibrationPhase phase,
        int targetSnapshot,
        DateTimeOffset startedUtc,
        long startedStopwatchTicks,
        int maximumEvents = DefaultMaximumEvents)
    {
        Phase = phase;
        TargetSnapshot = ClampTarget(targetSnapshot);
        StartedUtc = startedUtc;
        StartedStopwatchTicks = startedStopwatchTicks;
        _maximumEvents = Math.Max(1, maximumEvents);
    }

    public FreeSpinCalibrationPhase Phase { get; }
    public int TargetSnapshot { get; }
    public DateTimeOffset StartedUtc { get; }
    public long StartedStopwatchTicks { get; }
    public int EventCount => _events.Count;
    public int DroppedEventCount { get; private set; }

    public void Record(FreeSpinRawInputEvent input)
    {
        if (_events.Count >= _maximumEvents)
        {
            DroppedEventCount++;
            return;
        }

        var dx = _hasPreviousPosition ? input.X - _previousX : 0;
        var dy = _hasPreviousPosition ? input.Y - _previousY : 0;
        _previousX = input.X;
        _previousY = input.Y;
        _hasPreviousPosition = true;

        string? transition = null;
        if (input.Kind == FreeSpinRawEventKind.Button && input.Button != FreeSpinMouseButton.None)
        {
            var bit = 1 << ((int)input.Button - 1);
            if (input.IsButtonDown)
                _buttonMask |= bit;
            else
                _buttonMask &= ~bit;
            transition = PhaseLabel(input.Button) + (input.IsButtonDown ? "Down" : "Up");
        }

        _events.Add(new FreeSpinCalibrationEvent
        {
            Kind = input.Kind.ToString().ToLowerInvariant(),
            TimestampUtc = input.TimestampUtc,
            StopwatchTicks = input.StopwatchTicks,
            X = input.X,
            Y = input.Y,
            Dx = dx,
            Dy = dy,
            WheelDelta = input.WheelDelta,
            WheelAxis = input.WheelAxis,
            ButtonMask = _buttonMask,
            ButtonTransition = transition,
        });
    }

    public FreeSpinCalibrationSample Complete(DateTimeOffset completedUtc, long completedStopwatchTicks) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Phase = ToStorageLabel(Phase),
        StartedUtc = StartedUtc,
        CompletedUtc = completedUtc,
        StartedStopwatchTicks = StartedStopwatchTicks,
        CompletedStopwatchTicks = completedStopwatchTicks,
        TargetSnapshot = TargetSnapshot,
        DroppedEventCount = DroppedEventCount,
        Events = _events.ToList(),
    };

    /// <summary>Checks the manual-label contract without discarding an active session.</summary>
    public bool TryValidateCompletion(long completedStopwatchTicks, out string? validationMessage) =>
        FreeSpinCalibrationValidation.TryValidate(
            Phase,
            StartedStopwatchTicks,
            completedStopwatchTicks,
            _events,
            out validationMessage);

    public static int ClampTarget(int value) => Math.Max(30, Math.Min(50, value));

    public static string ToStorageLabel(FreeSpinCalibrationPhase phase) => phase switch
    {
        FreeSpinCalibrationPhase.Lift => "lift",
        FreeSpinCalibrationPhase.Landing => "landing",
        FreeSpinCalibrationPhase.Reposition => "reposition",
        FreeSpinCalibrationPhase.LegitimateScroll => "legitimate-scroll",
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

    private static string PhaseLabel(FreeSpinMouseButton button) => button.ToString().ToLowerInvariant();
}

/// <summary>Shared fail-closed persistence contract for manually labelled samples.</summary>
public static class FreeSpinCalibrationValidation
{
    public static bool TryValidate(FreeSpinCalibrationSample sample, out string? validationMessage)
    {
        if (!TryParsePhase(sample.Phase, out var phase))
        {
            validationMessage = "The sample phase is unknown.";
            return false;
        }
        return TryValidate(phase, sample.StartedStopwatchTicks, sample.CompletedStopwatchTicks, sample.Events, out validationMessage);
    }

    internal static bool TryValidate(FreeSpinCalibrationPhase phase, long startedTicks, long completedTicks,
        IEnumerable<FreeSpinCalibrationEvent> events, out string? validationMessage)
    {
        if (completedTicks - startedTicks < FreeSpinCalibrationSession.MinimumDurationStopwatchTicks)
        {
            validationMessage = "Wait at least 0.5 s before finishing.";
            return false;
        }
        if (phase == FreeSpinCalibrationPhase.LegitimateScroll && !events.Any(e => e.Kind == "wheel" && e.WheelDelta != 0))
        {
            validationMessage = "Perform at least one scroll before finishing the legitimate-scroll sample.";
            return false;
        }
        validationMessage = null;
        return true;
    }

    private static bool TryParsePhase(string value, out FreeSpinCalibrationPhase phase)
    {
        phase = value switch
        {
            "lift" => FreeSpinCalibrationPhase.Lift,
            "landing" => FreeSpinCalibrationPhase.Landing,
            "reposition" => FreeSpinCalibrationPhase.Reposition,
            "legitimate-scroll" => FreeSpinCalibrationPhase.LegitimateScroll,
            _ => default,
        };
        return value is "lift" or "landing" or "reposition" or "legitimate-scroll";
    }
}
