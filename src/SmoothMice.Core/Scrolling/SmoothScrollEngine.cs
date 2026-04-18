using SmoothMice.Core.Config;

namespace SmoothMice.Core.Scrolling;

/// <summary>Impulse + exponential decay; integral of output matches requested scroll distance.</summary>
public sealed class SmoothScrollEngine
{
    /// <summary>Velocity in wheel-delta units per millisecond (signed).</summary>
    private double _velocity;

    /// <summary>Sub-unit carry-over so fractional units are never lost.</summary>
    private double _fracAccum;

    private long _lastTickMs;
    private bool _hasLastTick;

    public void Reset()
    {
        _velocity = 0;
        _fracAccum = 0;
        _hasLastTick = false;
    }

    /// <summary>
    /// Feed a physical wheel delta. rawDelta is typically ±120 per notch.
    /// Acceleration multiplier is already applied externally.
    /// </summary>
    public void PushPhysicalDelta(int rawDelta, ScrollProfileSettings settings, double accel)
    {
        var totalUnits = rawDelta * ScrollMath.StepScale(settings.StepSizePx) * accel;

        var decayTau = ComputeDecayTau(settings);
        var impulse = totalUnits / decayTau;

        if (_velocity == 0 || Math.Sign(impulse) == Math.Sign(_velocity))
        {
            _velocity += impulse;
        }
        else
        {
            _velocity = impulse;
            _fracAccum = 0;
        }
    }

    /// <summary>
    /// Advance simulation by one tick; returns signed wheel-delta units to inject.
    /// </summary>
    public int Tick(long nowMs, ScrollProfileSettings settings)
    {
        if (!_hasLastTick)
        {
            _hasLastTick = true;
            _lastTickMs = nowMs;
            return 0;
        }

        var dt = Math.Clamp(nowMs - _lastTickMs, 0.0, 100.0);
        _lastTickMs = nowMs;

        if (IsQuiet())
        {
            _velocity = 0;
            _fracAccum = 0;
            return 0;
        }

        var decayTau = ComputeDecayTau(settings);

        var decayFactor = Math.Exp(-dt / decayTau);
        var emitted = _velocity * decayTau * (1.0 - decayFactor);
        _velocity *= decayFactor;

        if (Math.Abs(_velocity) < 1e-6) _velocity = 0;

        _fracAccum += emitted;
        var whole = (int)_fracAccum;
        _fracAccum -= whole;

        return whole;
    }

    public bool IsQuiet() =>
        Math.Abs(_velocity) < 1e-4 && Math.Abs(_fracAccum) < 0.5;

    private static double ComputeDecayTau(ScrollProfileSettings settings)
    {
        var tau = Math.Max(10.0, settings.AnimationTimeMs);

        if (!settings.AnimationEasing)
            return tau * 0.10;

        var stretch = Math.Clamp(settings.TailToHeadRatio, 0.1, 10.0);
        return tau * (0.30 + stretch * 0.18);
    }
}
