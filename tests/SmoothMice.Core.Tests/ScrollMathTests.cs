using SmoothMice.Core.Config;
using SmoothMice.Core.Diagnostics;
using SmoothMice.Core.Scrolling;
using Xunit;

namespace SmoothMice.Core.Tests;

public class ScrollMathTests
{
    [Fact]
    public void Scroll_diagnostics_marks_short_burst_and_rapid_reversal_per_axis()
    {
        var analyzer = new ScrollPulseDiagnosticAnalyzer(timestampFrequency: 1_000);
        var start = DateTimeOffset.Parse("2026-09-19T00:00:00Z");

        var first = analyzer.Analyze(new(start, 0, false, 120, false, 1, 2));
        var second = analyzer.Analyze(new(start, 30, false, 120, false, 1, 2));
        var third = analyzer.Analyze(new(start, 60, false, 120, false, 1, 2));
        var burst = analyzer.Analyze(new(start, 90, false, 120, false, 1, 2));
        var reversal = analyzer.Analyze(new(start, 120, false, -120, false, 1, 2));

        Assert.Null(first.IntervalMilliseconds);
        Assert.Equal(30, second.IntervalMilliseconds);
        Assert.False(third.IsShortBurst);
        Assert.True(burst.IsShortBurst);
        Assert.Equal(4, burst.BurstCount120Milliseconds);
        Assert.True(reversal.IsRapidReversal);
    }

    [Fact]
    public void Scroll_diagnostics_keeps_vertical_and_horizontal_intervals_separate_and_formats_ndjson()
    {
        var analyzer = new ScrollPulseDiagnosticAnalyzer(timestampFrequency: 1_000);
        var utc = DateTimeOffset.Parse("2026-09-19T00:00:00Z");
        _ = analyzer.Analyze(new(utc, 0, false, 120, true, 10, 20));
        var horizontal = analyzer.Analyze(new(utc, 10, true, -120, false, 11, 21));
        var vertical = analyzer.Analyze(new(utc, 200, false, 120, false, 12, 22));

        Assert.Null(horizontal.IntervalMilliseconds);
        Assert.Equal(200, vertical.IntervalMilliseconds);

        var line = ScrollPulseDiagnosticFormatter.FormatPulse(new(utc, 200, false, 120, false, 12, 22), vertical);
        Assert.Contains("\"kind\":\"pulse\"", line);
        Assert.Contains("\"axis\":\"vertical\"", line);
        Assert.Contains("\"interval_ms\":200", line);
        Assert.Contains("\"rapid_reversal\":false", line);
    }

    [Fact]
    public void Live_monitor_session_bounds_rows_marks_anomalies_and_clear_starts_fresh()
    {
        var session = new ScrollPulseMonitorSession(timestampFrequency: 1_000, maximumRows: 3);
        var utc = DateTimeOffset.Parse("2026-09-19T00:00:00Z");

        _ = session.Record(new(utc, 0, false, 120, false, 0, 0));
        _ = session.Record(new(utc, 30, false, 120, false, 0, 0));
        _ = session.Record(new(utc, 60, false, 120, false, 0, 0));
        var burst = session.Record(new(utc, 90, false, 120, false, 0, 0));
        var reversal = session.Record(new(utc, 120, false, -120, false, 0, 0));

        Assert.Equal(5, session.TotalPulses);
        Assert.Equal(3, session.Entries.Count);
        Assert.Equal(2, session.DiscardedRows);
        Assert.True(burst.Analysis.IsShortBurst);
        Assert.True(reversal.Analysis.IsRapidReversal);
        Assert.Equal(TimeSpan.FromMilliseconds(120), reversal.Elapsed);

        session.Clear();

        Assert.Empty(session.Entries);
        Assert.Equal(0, session.TotalPulses);
        Assert.Equal(0, session.DiscardedRows);
        var fresh = session.Record(new(utc, 500, true, -120, false, 0, 0));
        Assert.Null(fresh.Analysis.IntervalMilliseconds);
        Assert.Equal(TimeSpan.Zero, fresh.Elapsed);
    }

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
        engine.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);

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
        engNoEase.PushPhysicalDelta(120, noEase, 1.0, nowMs: 0);
        var firstNoEase = Math.Abs(engNoEase.Tick(4, noEase));

        var engEase = new SmoothScrollEngine();
        engEase.Tick(0, ease);
        engEase.PushPhysicalDelta(120, ease, 1.0, nowMs: 0);
        var firstEase = Math.Abs(engEase.Tick(4, ease));

        Assert.True(firstNoEase > firstEase,
            $"No-easing first tick ({firstNoEase}) should exceed easing ({firstEase})");
    }

    [Fact]
    public void Engine_bounds_a_freespin_burst_without_invalid_or_backlogged_tick_deltas()
    {
        const int maximumPendingDeltaUnits = 48_000;
        const int maximumDeltaPerTick = 1_920;
        var settings = new ScrollProfileSettings
        {
            StepSizePx = 80,
            AnimationTimeMs = 150,
            AnimationEasing = false,
        };
        var engine = new SmoothScrollEngine();

        // 10,000 one-millisecond pulses represent a much larger burst than a physical
        // FreeSpin can deliver to the 4 ms animation loop. It must remain bounded.
        for (var i = 0; i < 10_000; i++)
            engine.PushPhysicalDelta(120, settings, accel: 3.5, nowMs: i);

        var total = 0;
        for (var tick = 0; tick < 2_000 && !engine.IsQuiet(); tick++)
        {
            var delta = engine.Tick(tick * 4L, settings);
            Assert.InRange(delta, -maximumDeltaPerTick, maximumDeltaPerTick);
            total += Math.Abs(delta);
        }

        Assert.True(engine.IsQuiet());
        Assert.InRange(total, 0, maximumPendingDeltaUnits + maximumDeltaPerTick);
    }
}
