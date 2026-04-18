namespace SmoothMice.Core.Scrolling;

public static class ScrollMath
{
    /// <summary>Scales raw delta by stepSizePx relative to a 20 px baseline.</summary>
    public static double StepScale(double stepSizePx) =>
        stepSizePx <= 0 ? 1.0 : stepSizePx / 20.0;

    /// <summary>Power-curve multiplier from EWMA-smoothed scroll interval (see <c>ScrollCoordinator</c>).</summary>
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

    /// <summary>Returns (exponent, maxX) for the named preset.</summary>
    public static (double exponent, double maxX) PresetValues(int preset) => preset switch
    {
        0 => (1.0, 2.5),  // Linear   — proportional, gentle ceiling
        1 => (1.3, 3.5),  // Smooth   — moderate curve, Mac-like
        2 => (2.0, 6.0),  // Exponential — aggressive burst
        _ => (1.3, 3.5),
    };
}
