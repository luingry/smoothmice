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

        // SmoothScroll never decelerates below the physical notch size — it only ever multiplies
        // up. A floor below 1.0 here shrank slower, evenly-paced scrolling to a fraction of its
        // distance, which is a direct cause of paced scrolling feeling weak/inconsistent.
        const double floor = 1.0;
        return Math.Min(Math.Max(raw, floor), Math.Max(1.0, maxX));
    }

    /// <summary>
    /// Advances the EWMA-smoothed scroll interval used by <c>AccelerationMultiplier</c>'s
    /// <c>smoothedIntervalMs</c> input (see <c>ScrollCoordinator.UpdateEwma</c>).
    /// </summary>
    /// <param name="wasQuiet">
    /// True when the previous animation had already finished (both engines quiet) before this
    /// event arrived — the real signal that this event starts a brand-new gesture, as opposed to
    /// an arbitrary wall-clock timeout. A deliberate, paced scroll (one notch, pause, one notch,
    /// ...) must feel identical on every notch: without resetting on this signal, any pause
    /// shorter than <paramref name="resetPauseMs"/> but longer than the reference interval
    /// dragged the EWMA above the reference, so only the very first event (or one after a long
    /// idle) ever got the neutral 1.0x multiplier while every paced repeat after it decayed
    /// toward the deceleration floor.
    /// </param>
    public static double UpdateSmoothedInterval(
        double currentSmoothedMs,
        long lastEventMs,
        long nowMs,
        bool wasQuiet,
        double referenceIntervalMs,
        double alpha = 0.55,
        double maxIntervalMs = 1200.0,
        double resetPauseMs = 1500.0)
    {
        if (lastEventMs < 0 || wasQuiet || nowMs - lastEventMs >= resetPauseMs)
            return referenceIntervalMs;

        var actual = Math.Min(nowMs - lastEventMs, maxIntervalMs);
        return alpha * actual + (1.0 - alpha) * currentSmoothedMs;
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
