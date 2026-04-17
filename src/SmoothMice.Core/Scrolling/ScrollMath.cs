namespace SmoothMice.Core.Scrolling;

public static class ScrollMath
{
    /// <summary>Windows wheel delta quantum (one standard notch).</summary>
    public const int WheelDelta = 120;

    /// <summary>Scales raw delta by stepSizePx relative to a 20 px baseline.</summary>
    public static double StepScale(double stepSizePx) =>
        stepSizePx <= 0 ? 1.0 : stepSizePx / 20.0;

    // ── Acceleration ──────────────────────────────────────────────────────

    /// <summary>
    /// Smooth power-curve acceleration multiplier.
    ///
    /// Uses a pre-computed EWMA of inter-event intervals (maintained by the
    /// caller — see ScrollCoordinator) instead of a raw event window, so
    /// speed changes are felt gradually and there are no discrete tier
    /// boundaries to cause stepping artefacts.
    ///
    ///   speed = referenceIntervalMs / smoothedIntervalMs
    ///   mult  = clamp( speed ^ exponent, 0.10, maxX )
    ///
    /// Example curves with referenceIntervalMs = 400 ms:
    ///
    ///   interval  speed   exp=1.0  exp=1.3  exp=2.0
    ///   1600 ms   0.25×   0.25×    0.18×    0.06×   (very slow)
    ///    800 ms   0.50×   0.50×    0.41×    0.25×   (slow)
    ///    400 ms   1.00×   1.00×    1.00×    1.00×   (reference)
    ///    200 ms   2.00×   2.00×    2.46×    4.00×   (fast)
    ///    100 ms   4.00×   4.00×    6.06×   16.00×   (very fast — capped at maxX)
    /// </summary>
    public static double AccelerationMultiplier(
        double smoothedIntervalMs,
        double referenceIntervalMs,
        double exponent,
        double maxX)
    {
        if (exponent <= 0) return 1.0;

        var speed = referenceIntervalMs / Math.Max(smoothedIntervalMs, 5.0);
        var raw   = Math.Pow(speed, exponent);

        const double floor = 0.10;
        return Math.Clamp(raw, floor, Math.Max(1.0, maxX));
    }

    // ── Presets ───────────────────────────────────────────────────────────

    /// <summary>Returns (exponent, maxX) for the named preset.</summary>
    public static (double exponent, double maxX) PresetValues(int preset) => preset switch
    {
        0 => (1.0, 2.5),  // Linear   — proportional, gentle ceiling
        1 => (1.3, 3.5),  // Smooth   — moderate curve, Mac-like
        2 => (2.0, 6.0),  // Exponential — aggressive burst
        _ => (1.3, 3.5),
    };
}
