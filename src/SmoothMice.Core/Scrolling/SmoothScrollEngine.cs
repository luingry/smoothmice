using SmoothMice.Core.Config;

namespace SmoothMice.Core.Scrolling;

/// <summary>
/// Velocity-impulse smooth scroll engine.
///
/// Each physical wheel event applies an impulse proportional to the desired
/// total scroll units.  The velocity then decays exponentially, producing
/// per-tick output that forms a natural ease-out curve.
///
/// Key property: total emitted = v0 * decayTau = totalUnits (exact integral),
/// so the desired scroll distance is always honoured regardless of easing.
/// </summary>
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
        var sign = settings.ReverseWheelDirection ? -1 : 1;
        var totalUnits = rawDelta * ScrollMath.StepScale(settings.StepSizePx) * accel * sign;

        // Choose decay time constant based on current settings.
        var decayTau = ComputeDecayTau(settings);

        // Impulse that integrates to exactly totalUnits over the decay lifetime.
        var impulse = totalUnits / decayTau;

        if (_velocity == 0 || Math.Sign(impulse) == Math.Sign(_velocity))
        {
            // Same direction or idle: accumulate momentum.
            _velocity += impulse;
        }
        else
        {
            // Direction reversal: discard carry-over and start fresh.
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

        // Exact integral: ∫₀^dt  v₀·e^(−t/τ) dt  =  v₀·τ·(1 − e^(−dt/τ))
        var decayFactor = Math.Exp(-dt / decayTau);
        var emitted = _velocity * decayTau * (1.0 - decayFactor);
        _velocity *= decayFactor;

        if (Math.Abs(_velocity) < 1e-6) _velocity = 0;

        // Accumulate fractional units and emit whole delta units only.
        _fracAccum += emitted;
        var whole = (int)_fracAccum;   // C# truncates toward zero — correct for ±
        _fracAccum -= whole;

        return whole;
    }

    public bool IsQuiet() =>
        Math.Abs(_velocity) < 1e-4 && Math.Abs(_fracAccum) < 0.5;

    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compute the exponential decay time-constant (τ) from current settings.
    ///
    ///  • AnimationEasing = false → τ = tau × 0.10  (very fast, snappy)
    ///  • AnimationEasing = true  → τ = tau × (0.30 + tailRatio × 0.18)
    ///    so TailToHeadRatio = 1  → τ = 0.48 × tau   (short tail)
    ///       TailToHeadRatio = 3  → τ = 0.84 × tau   (default, smooth)
    ///       TailToHeadRatio = 10 → τ = 2.1  × tau   (very long tail)
    ///
    /// The animation is therefore dramatically different with easing on vs off.
    /// </summary>
    private static double ComputeDecayTau(ScrollProfileSettings settings)
    {
        var tau = Math.Max(10.0, settings.AnimationTimeMs);

        if (!settings.AnimationEasing)
            return tau * 0.10; // no easing: fast, almost immediate

        var stretch = Math.Clamp(settings.TailToHeadRatio, 0.1, 10.0);
        return tau * (0.30 + stretch * 0.18);
    }
}
