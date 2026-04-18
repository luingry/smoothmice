namespace SmoothMice.Core.Config;

/// <summary>Per-profile scroll tuning.</summary>
public sealed class ScrollProfileSettings
{
    // ── Animation ─────────────────────────────────────────────────────────
    public double StepSizePx { get; set; } = 20;
    public int AnimationTimeMs { get; set; } = 100;
    public double TailToHeadRatio { get; set; } = 3;
    public bool AnimationEasing { get; set; } = true;

    // ── Acceleration ──────────────────────────────────────────────────────
    /// <summary>
    /// Reference scroll interval in ms. When your actual scroll speed equals
    /// this interval, multiplier = 1.0×.  Default 400 ms ≈ 2.5 notches/s.
    /// </summary>
    public int AccelerationDeltaMs { get; set; } = 400;

    /// <summary>
    /// Curve shape preset used to fill Exponent + MaxX quickly.
    /// 0 = Linear, 1 = Smooth (default), 2 = Exponential.
    /// </summary>
    public int AccelerationCurvePreset { get; set; } = 1;

    /// <summary>
    /// Power-curve exponent for acceleration.
    /// 0 = off (always 1×), 1 = linear, 1.3 = smooth (default), 2 = exponential.
    /// </summary>
    public double AccelerationExponent { get; set; } = 1.3;

    /// <summary>Upper cap on the acceleration multiplier.</summary>
    public double AccelerationMaxX { get; set; } = 3.5;

    // ── Scrolling behaviour ───────────────────────────────────────────────
    public bool EnableForAllAppsByDefault { get; set; } = true;
    public bool HorizontalSmoothness { get; set; } = true;

    public ScrollProfileSettings Clone() => (ScrollProfileSettings)MemberwiseClone();
}
