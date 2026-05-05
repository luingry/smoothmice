using SmoothMice.Core.Config;

namespace SmoothMice.Core.Scrolling;

/// <summary>
/// Velocity-lerp smooth scroll engine.
///
/// State: a single <c>_remaining</c> scalar (signed units still to emit) plus a
/// <c>_speed</c> ramp factor [0, 1] that provides the ease-in envelope.
///
/// Each tick emits <c>remaining × lerpFactor × speed</c> units, then:
///   • <c>speed</c>  ramps toward 1.0  → smooth ease-in over the first ~20–30 ms
///   • remaining shrinks exponentially  → natural ease-out tail
///
/// Advantages over the previous step-queue model:
///   • Multiple rapid events accumulate in <c>_remaining</c> naturally — no
///     overlapping step curves that produce mid-animation velocity "bumps".
///   • Ease-in and ease-out are applied to the combined motion, not per-event.
///   • C¹ and C² continuous: no jerk at the inflection point.
///   • Simpler state: one scalar instead of a list of structs.
///   • Settings changes (AnimationTimeMs, TailToHeadRatio) take effect
///     immediately on the next tick.
/// </summary>
public sealed class SmoothScrollEngine
{
    private double _remaining;   // signed units still to emit
    private double _speed;       // [0, 1] ease-in ramp factor
    private double _fracAccum;   // fractional carry for integer output

    public void Reset()
    {
        _remaining = 0;
        _speed     = 0;
        _fracAccum = 0;
    }

    /// <summary>
    /// Feed a physical wheel delta.
    /// <paramref name="nowMs"/> is accepted for API compatibility but not used
    /// in this model (timing is implicit via tick cadence).
    /// </summary>
    public void PushPhysicalDelta(int rawDelta, ScrollProfileSettings settings, double accel, long nowMs)
    {
        if (rawDelta == 0) return;

        var units = rawDelta * ScrollMath.StepScale(settings.StepSizePx) * accel;

        if (_remaining != 0.0 && Math.Sign(_remaining) != Math.Sign(units))
        {
            // Direction reversal: discard old motion and start fresh.
            _remaining = 0;
            _fracAccum = 0;
            _speed     = 0;
        }
        else if (_remaining != 0.0 && _speed < 0.3)
        {
            // Mid-animation but nearly stopped: nudge speed up so new input
            // feels responsive instead of sluggish.
            _speed = 0.3;
        }

        _remaining += units;
    }

    /// <summary>Advance animation by one tick; returns signed wheel-delta units to inject.</summary>
    public int Tick(long nowMs, ScrollProfileSettings settings)
    {
        if (Math.Abs(_remaining) < 0.01) return 0;

        var lerp = LerpFactor(settings.AnimationTimeMs);

        double ramp;
        if (settings.AnimationEasing)
        {
            var tailToHead = Math.Min(Math.Max(settings.TailToHeadRatio, 0.5), 12.0);
            ramp = SpeedRampFactor(lerp, tailToHead);
        }
        else
        {
            ramp = 1.0; // no ease-in — only the natural exponential ease-out
        }

        // Advance speed toward 1.0 (ease-in envelope).
        _speed += (1.0 - _speed) * ramp;

        // Emit fraction of remaining, scaled by current speed.
        var delta = _remaining * lerp * _speed;

        _remaining -= delta;

        // Flush the trailing tail once it's too small to matter.
        if (Math.Abs(_remaining) < 0.1)
        {
            delta     += _remaining;
            _remaining = 0;
            _speed     = 0;
        }

        _fracAccum += delta;
        var whole = (int)_fracAccum;
        _fracAccum -= whole;
        return whole;
    }

    public bool IsQuiet() => Math.Abs(_remaining) < 0.1;

    /// <summary>
    /// Per-tick lerp factor such that 98 % of <c>_remaining</c> is consumed in
    /// <paramref name="animTimeMs"/> milliseconds at full speed (4 ms / tick).
    /// </summary>
    private static double LerpFactor(int animTimeMs)
    {
        var ticks = Math.Max(10, animTimeMs) / 4.0;
        return 1.0 - Math.Pow(0.02, 1.0 / ticks);
    }

    /// <summary>
    /// Speed-ramp rate per tick, chosen so that the ease-in phase occupies
    /// <c>1 / (1 + tailToHead)</c> of the total animation duration.
    /// E.g. tailToHead = 3 → 25 % ease-in, 75 % ease-out.
    /// </summary>
    private static double SpeedRampFactor(double lerp, double tailToHead)
    {
        // Total ticks for 98 % completion at full speed.
        var totalTicks = Math.Log(0.02) / Math.Log(1.0 - lerp);

        // Ticks allocated for the acceleration (ease-in) phase.
        var accelTicks = Math.Max(1.0, totalTicks / (1.0 + tailToHead));

        // Ramp rate so speed reaches 95 % within accelTicks ticks.
        return 1.0 - Math.Pow(0.05, 1.0 / accelTicks);
    }
}
