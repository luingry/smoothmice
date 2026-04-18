using SmoothMice.Core.Config;
using SmoothMice.Core.Scrolling;
using Xunit;

namespace SmoothMice.Core.Tests;

public class ScrollMathTests
{
    // ── StepScale ─────────────────────────────────────────────────────────

    [Fact]
    public void StepScale_maps_baseline()
    {
        Assert.Equal(1.0, ScrollMath.StepScale(20), 5);
        Assert.Equal(2.0, ScrollMath.StepScale(40), 5);
    }

    // ── AccelerationMultiplier (power-curve + EWMA) ───────────────────────

    [Fact]
    public void AccelMult_reference_speed_returns_one()
    {
        // smoothed == reference → speed = 1.0 → 1.0^exponent = 1.0
        Assert.Equal(1.0, ScrollMath.AccelerationMultiplier(400, 400, exponent: 1.3, maxX: 3.5), 5);
    }

    [Fact]
    public void AccelMult_no_exponent_always_returns_one()
    {
        Assert.Equal(1.0, ScrollMath.AccelerationMultiplier(100, 400, exponent: 0, maxX: 3.5), 5);
        Assert.Equal(1.0, ScrollMath.AccelerationMultiplier(800, 400, exponent: 0, maxX: 3.5), 5);
    }

    [Fact]
    public void AccelMult_fast_scroll_exceeds_one()
    {
        // 100 ms interval with 400 ms reference → speed 4 → > 1×
        var m = ScrollMath.AccelerationMultiplier(100, 400, exponent: 1.3, maxX: 3.5);
        Assert.True(m > 1.0, $"fast scroll should exceed 1× (got {m:F3})");
    }

    [Fact]
    public void AccelMult_fast_scroll_capped_at_maxX()
    {
        // Very fast → capped at maxX = 3.5
        var m = ScrollMath.AccelerationMultiplier(10, 400, exponent: 1.3, maxX: 3.5);
        Assert.Equal(3.5, m, 3);
    }

    [Fact]
    public void AccelMult_slow_scroll_is_below_one()
    {
        // 1600 ms / 400 ms reference = speed 0.25 → well below 1×
        var m = ScrollMath.AccelerationMultiplier(1600, 400, exponent: 1.3, maxX: 3.5);
        Assert.True(m < 1.0, $"slow scroll should be < 1× (got {m:F3})");
        Assert.True(m >= 0.10, "should not drop below floor");
    }

    [Fact]
    public void AccelMult_is_continuous_across_speed_range()
    {
        // Monotonically increasing: faster interval → higher multiplier
        double prev = 0;
        foreach (var interval in new[] { 1600.0, 800, 400, 200, 100 })
        {
            var m = ScrollMath.AccelerationMultiplier(interval, 400, exponent: 1.3, maxX: 3.5);
            Assert.True(m >= prev, $"multiplier should be non-decreasing (prev={prev:F3}, now={m:F3} at interval={interval})");
            prev = m;
        }
    }

    // ── Presets ───────────────────────────────────────────────────────────

    [Fact]
    public void Preset_smooth_has_expected_defaults()
    {
        var (exp, max) = ScrollMath.PresetValues(1);
        Assert.Equal(1.3, exp, 5);
        Assert.Equal(3.5, max, 5);
    }

    // ── SmoothScrollEngine ────────────────────────────────────────────────

    [Fact]
    public void Engine_emits_correct_total_units()
    {
        var settings = new ScrollProfileSettings
        {
            StepSizePx = 20, AnimationTimeMs = 100,
            AnimationEasing = false, TailToHeadRatio = 1,
        };

        var engine = new SmoothScrollEngine();
        engine.PushPhysicalDelta(120, settings, accel: 1.0);

        int total = 0;
        long t = 0;
        for (int i = 0; i < 500 && !engine.IsQuiet(); i++) { t += 4; total += engine.Tick(t, settings); }

        Assert.InRange(total, 118, 122);
    }

    [Fact]
    public void Engine_easing_emits_less_in_first_tick_than_no_easing()
    {
        var noEase = new ScrollProfileSettings
            { StepSizePx = 80, AnimationTimeMs = 150, TailToHeadRatio = 3, AnimationEasing = false };
        var ease = new ScrollProfileSettings
            { StepSizePx = 80, AnimationTimeMs = 150, TailToHeadRatio = 3, AnimationEasing = true };

        var engNoEase = new SmoothScrollEngine();
        engNoEase.Tick(0, noEase);
        engNoEase.PushPhysicalDelta(120, noEase, 1.0);
        var firstNoEase = Math.Abs(engNoEase.Tick(4, noEase));

        var engEase = new SmoothScrollEngine();
        engEase.Tick(0, ease);
        engEase.PushPhysicalDelta(120, ease, 1.0);
        var firstEase = Math.Abs(engEase.Tick(4, ease));

        Assert.True(firstNoEase > firstEase,
            $"No-easing first tick ({firstNoEase}) should exceed easing ({firstEase})");
    }
}
