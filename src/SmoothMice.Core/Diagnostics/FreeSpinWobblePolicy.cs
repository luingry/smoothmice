using System.Diagnostics;

namespace SmoothMice.Core.Diagnostics;

public enum FreeSpinWobbleAction
{
    /// <summary>Deliver the pulse normally.</summary>
    Pass,
    /// <summary>Swallow the pulse permanently.</summary>
    Suppress,
}

public sealed class FreeSpinWobbleDecision
{
    public FreeSpinWobbleAction Action { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Rule-based inertia filter measured from labelled raw sessions (all movement and wheel input,
/// not short F8 captures). Every pulse is decided the moment it arrives: holding pulses to wait
/// for evidence delayed the start of real scrolls, and replaying them made the smoothing
/// acceleration see a near-zero interval and jump.
/// <para>
/// Lifting the mouse rocks the wheel: a pulse while the hand jerks the mouse up (tracking stops
/// 10-300 ms before it), then usually an opposite pulse 60-350 ms later, sometimes more. Some lifts
/// spin one way only. A real scroll is a run of pulses 7-32 ms apart that rarely reverses within
/// 350 ms; when it follows movement, the cursor is still moving or decelerated to a stop.
/// </para>
/// <list type="bullet">
/// <item>Lift start: a gesture-start pulse 10-300 ms after movement whose last 30 ms were fast
/// (≥ <see cref="SuspectMinimumEndSpeed"/>) is dropped and starts an inertia episode.</item>
/// <item>Rebound: an opposite pulse within <see cref="ReboundMs"/> of a gesture start (even one
/// that passed) is dropped and starts an episode.</item>
/// <item>Episode: after a reversal, further pulses within <see cref="ReboundMs"/> of each other are
/// dropped unless they come at real-scroll speed (≤ <see cref="FastScrollGapMs"/>). Without a
/// reversal (one-way spin) only <see cref="MaximumOneWayFollowUps"/> slow same-direction pulse is
/// dropped, because a slow notch-by-notch scroll looks the same.</item>
/// </list>
/// Vertical wheel only. Holding a button disables the filter.
/// </summary>
public sealed class FreeSpinWobblePolicy
{
    public const double GestureGapMs = 300;
    public const double ReboundMs = 350;
    public const double MovementWindowMs = 300;
    public const double SuspectMinimumPathPx = 1;
    public const double MaximumEpisodeMs = 1_000;
    public const double EndSpeedWindowMs = 30;

    /// <summary>A gesture-start pulse at most this long after movement can be a lift (negative = never).</summary>
    public double SuspectMoveAgeMs { get; set; } = 300;
    /// <summary>Movement more recent than this means the user scrolls while moving the cursor, not lifting.</summary>
    public double SuspectMinimumMoveAgeMs { get; set; } = 10;
    /// <summary>
    /// Speed (px/s) over the last <see cref="EndSpeedWindowMs"/> of movement required for a lift:
    /// lifting jerks the mouse, a cursor stopped to scroll decelerates (0 = off).
    /// </summary>
    public double SuspectMinimumEndSpeed { get; set; } = 250;
    /// <summary>Pulses this close together in one direction are a real scroll.</summary>
    public double FastScrollGapMs { get; set; } = 45;
    /// <summary>Slow same-direction pulses dropped after a dropped start when the wheel never reversed.</summary>
    public int MaximumOneWayFollowUps { get; set; } = 1;

    private readonly double _ticksPerMs = Stopwatch.Frequency / 1000d;
    private readonly Queue<(long Ticks, double Length)> _segments = new();
    private double _pathInWindow;
    private bool _hasMove;
    private long _lastMoveTicks;
    private int _lastX, _lastY;
    private int _buttonMask;

    private bool _hasPulse;
    private long _lastPulseTicks;
    private int _lastSign;
    // A passed gesture start that a rebound may still follow.
    private bool _startAnchor;
    private long _startTicks;
    private int _startSign;
    // Inertia episode.
    private bool _episode;
    private long _episodeStartTicks;
    private bool _episodeReversed;
    private int _oneWayFollowUps;

    public void Reset()
    {
        _segments.Clear(); _pathInWindow = 0; _hasMove = false; _buttonMask = 0;
        _hasPulse = false; _startAnchor = false; _episode = false;
    }

    public void ObserveMove(long ticks, int x, int y)
    {
        if (_hasMove)
        {
            var length = Math.Sqrt((x - _lastX) * (double)(x - _lastX) + (y - _lastY) * (double)(y - _lastY));
            if (length > 0) { _segments.Enqueue((ticks, length)); _pathInWindow += length; }
        }
        _hasMove = true; _lastMoveTicks = ticks; _lastX = x; _lastY = y;
        Prune(ticks);
    }

    public void ObserveButton(int bit, bool down)
    {
        if (down) _buttonMask |= bit; else _buttonMask &= ~bit;
    }

    /// <summary>Movement length within the window, and ms since the last move (null: none yet).</summary>
    public (double PathPx, double? MoveAgeMs) Movement(long ticks)
    {
        Prune(ticks);
        return (_pathInWindow, _hasMove ? (ticks - _lastMoveTicks) / _ticksPerMs : null);
    }

    public FreeSpinWobbleDecision OnWheel(long ticks, int delta, bool horizontal)
    {
        var sign = Math.Sign(delta);
        if (horizontal || sign == 0 || _buttonMask != 0)
        {
            _startAnchor = false; _episode = false;
            return Done(ticks, sign, FreeSpinWobbleAction.Pass, "Filter inactive for this event.");
        }

        var sincePrevious = _hasPulse ? (ticks - _lastPulseTicks) / _ticksPerMs : double.MaxValue;
        var newGesture = sincePrevious > GestureGapMs;

        if (_episode && !newGesture && sincePrevious <= ReboundMs && (ticks - _episodeStartTicks) / _ticksPerMs <= MaximumEpisodeMs)
        {
            if (sign != _lastSign)
            {
                _episodeReversed = true;
                return Done(ticks, sign, FreeSpinWobbleAction.Suppress, $"Wheel rebound {sincePrevious:F0} ms after the previous pulse: inertia.");
            }
            if (sincePrevious > FastScrollGapMs)
            {
                if (_episodeReversed)
                    return Done(ticks, sign, FreeSpinWobbleAction.Suppress, $"Slow pulse {sincePrevious:F0} ms into a rocking wheel: inertia.");
                if (_oneWayFollowUps < MaximumOneWayFollowUps)
                {
                    _oneWayFollowUps++;
                    return Done(ticks, sign, FreeSpinWobbleAction.Suppress, $"Slow same-direction pulse {sincePrevious:F0} ms after a dropped pulse: one-way inertia.");
                }
            }
        }
        _episode = false;

        if (_startAnchor && !newGesture && sign == -_startSign && (ticks - _startTicks) / _ticksPerMs <= ReboundMs)
        {
            _startAnchor = false;
            BeginEpisode(_startTicks, reversed: true);
            return Done(ticks, sign, FreeSpinWobbleAction.Suppress, $"Wheel rebound {(ticks - _startTicks) / _ticksPerMs:F0} ms after the first pulse: inertia.");
        }
        _startAnchor = false;

        if (!newGesture)
            return Done(ticks, sign, FreeSpinWobbleAction.Pass, "Continuing scroll.");

        var (path, age) = Movement(ticks);
        if (age is double moveAge && moveAge <= SuspectMoveAgeMs && moveAge >= SuspectMinimumMoveAgeMs && path >= SuspectMinimumPathPx)
        {
            var endSpeed = EndSpeed();
            if (SuspectMinimumEndSpeed <= 0 || endSpeed >= SuspectMinimumEndSpeed)
            {
                BeginEpisode(ticks, reversed: false);
                return Done(ticks, sign, FreeSpinWobbleAction.Suppress, $"First pulse {moveAge:F0} ms after a sharp movement ({endSpeed:F0} px/s): lift inertia.");
            }
        }
        _startAnchor = true; _startTicks = ticks; _startSign = sign;
        return Done(ticks, sign, FreeSpinWobbleAction.Pass, age is double a && a < SuspectMinimumMoveAgeMs ? "First pulse while the cursor is moving." : "First pulse without a lift-like movement.");
    }

    private void BeginEpisode(long startTicks, bool reversed)
    {
        _episode = true; _episodeStartTicks = startTicks; _episodeReversed = reversed; _oneWayFollowUps = 0;
    }

    private FreeSpinWobbleDecision Done(long ticks, int sign, FreeSpinWobbleAction action, string reason)
    {
        _lastPulseTicks = ticks; _hasPulse = true; _lastSign = sign;
        return new FreeSpinWobbleDecision { Action = action, Reason = reason };
    }

    /// <summary>Path per second over the last <see cref="EndSpeedWindowMs"/> before the last move.</summary>
    private double EndSpeed()
    {
        var from = _lastMoveTicks - (long)(EndSpeedWindowMs * _ticksPerMs);
        double length = 0; long first = long.MaxValue;
        foreach (var (segTicks, segLength) in _segments)
            if (segTicks >= from) { length += segLength; if (segTicks < first) first = segTicks; }
        if (first == long.MaxValue) return 0;
        var spanMs = Math.Max(1, (_lastMoveTicks - first) / _ticksPerMs);
        return length / (spanMs / 1000d);
    }

    private void Prune(long ticks)
    {
        var limit = ticks - (long)(MovementWindowMs * _ticksPerMs);
        while (_segments.Count > 0 && _segments.Peek().Ticks < limit) _pathInWindow -= _segments.Dequeue().Length;
        if (_segments.Count == 0) _pathInWindow = 0;
    }
}
