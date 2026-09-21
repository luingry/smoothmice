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
    public void AccelMult_slow_scroll_never_drops_below_one()
    {
        // SmoothScroll never decelerates below the physical notch size — it only ever
        // multiplies up. 1600 ms / 400 ms reference = speed 0.25, well below the reference
        // speed, but the multiplier must clamp at the neutral 1.0× floor, not shrink further.
        var m = ScrollMath.AccelerationMultiplier(1600, 400, exponent: 1.3, maxX: 3.5);
        Assert.Equal(1.0, m, 5);
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

    // ── UpdateSmoothedInterval (paced-scroll consistency) ───────────────────

    [Fact]
    public void UpdateSmoothedInterval_resets_to_reference_when_engine_was_quiet()
    {
        // A gesture that starts once the previous animation has fully settled must always get
        // the neutral 1.0x baseline, no matter how short the pause before it was.
        var smoothed = ScrollMath.UpdateSmoothedInterval(
            currentSmoothedMs: 155.25, lastEventMs: 1_000, nowMs: 1_300, wasQuiet: true, referenceIntervalMs: 400);
        Assert.Equal(400, smoothed, 5);
    }

    [Fact]
    public void UpdateSmoothedInterval_paced_isolated_notches_stay_at_reference()
    {
        // Regression for "first scroll bigger than the rest": simulate several isolated,
        // paced notches (300 ms apart, each starting from a quiet engine) and confirm every
        // one gets the same neutral multiplier — not just the first.
        double smoothed = 400;
        long lastEventMs = -1;
        long nowMs = 0;
        for (var i = 0; i < 5; i++)
        {
            smoothed = ScrollMath.UpdateSmoothedInterval(smoothed, lastEventMs, nowMs, wasQuiet: true, referenceIntervalMs: 400);
            var accel = ScrollMath.AccelerationMultiplier(smoothed, 400, exponent: 1.3, maxX: 3.5);
            Assert.Equal(1.0, accel, 5);
            lastEventMs = nowMs;
            nowMs += 300;
        }
    }

    [Fact]
    public void UpdateSmoothedInterval_without_quiet_signal_drifts_above_reference_on_a_paced_repeat()
    {
        // Documents the bug this fixes: without the wasQuiet signal (simulated here by always
        // passing wasQuiet: false), a pause shorter than resetPauseMs (1500 ms) but longer than
        // the reference interval (400 ms) drags the EWMA-smoothed interval up, away from the
        // neutral reference — only the very first event stayed at the reference value.
        // (The resulting multiplier no longer dips below 1.0x here since AccelerationMultiplier's
        // floor was raised to 1.0 — SmoothScroll never decelerates — so this asserts directly on
        // the smoothed interval, which is what actually drifts.)
        var first = ScrollMath.UpdateSmoothedInterval(400, lastEventMs: -1, nowMs: 0, wasQuiet: false, referenceIntervalMs: 400);
        Assert.Equal(400, first, 5);

        var second = ScrollMath.UpdateSmoothedInterval(first, lastEventMs: 0, nowMs: 800, wasQuiet: false, referenceIntervalMs: 400);
        Assert.True(second > first, $"expected the smoothed interval to drift above the reference (first={first:F1}, second={second:F1})");

        var firstAccel = ScrollMath.AccelerationMultiplier(first, 400, exponent: 1.3, maxX: 3.5);
        var secondAccel = ScrollMath.AccelerationMultiplier(second, 400, exponent: 1.3, maxX: 3.5);
        Assert.Equal(1.0, firstAccel, 5);
        Assert.Equal(1.0, secondAccel, 5);
    }

    [Fact]
    public void UpdateSmoothedInterval_resets_after_a_long_wall_clock_pause_even_without_quiet()
    {
        var smoothed = ScrollMath.UpdateSmoothedInterval(
            currentSmoothedMs: 155.25, lastEventMs: 0, nowMs: 1_500, wasQuiet: false, referenceIntervalMs: 400);
        Assert.Equal(400, smoothed, 5);
    }

    // NOTE: the old `CoordinatorSequence_scroll_into_a_still_finishing_tail_stays_neutral_even_
    // with_a_short_real_interval` test asserted on `SmoothScrollEngine.IsNegligibleTailRelativeTo`,
    // a heuristic that existed only to patch the old shared-state engine's history-dependent
    // behavior. The new pulse-queue engine has no such heuristic (and no such method) — every
    // notch always contributes its own independent, full-size pulse regardless of what else is
    // animating, so there is nothing for the EWMA to special-case. See the new "Consistency" and
    // "No jump on resume" tests below for the properties that replace it.

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

    // ── New pulse-queue engine properties ───────────────────────────────────
    //
    // These lock in the properties the rewrite exists to guarantee: superposition of
    // independent, time-driven per-notch pulses instead of one shared, history-dependent ramp.
    // Every test drives `nowMs` explicitly — the engine has no implicit tick cadence.

    private static ScrollProfileSettings PulseTestSettings() => new()
    {
        StepSizePx = 80, AnimationTimeMs = 150, TailToHeadRatio = 3, AnimationEasing = true,
    };

    private static double RunToQuiet(SmoothScrollEngine engine, ScrollProfileSettings settings, ref long t, long stepMs = 4, int maxTicks = 2000)
    {
        double total = 0;
        for (var i = 0; i < maxTicks && !engine.IsQuiet(); i++)
        {
            t += stepMs;
            total += engine.Tick(t, settings);
        }
        return total;
    }

    [Fact]
    public void Engine_single_notch_delivers_the_same_total_regardless_of_when_it_lands()
    {
        // Consistency: one notch on a quiet engine, and an identical notch pushed at various
        // points into another notch's in-flight animation, must EACH contribute the same total
        // distance. This is the core property the old shared-`_speed` model could not guarantee
        // (measured ~4x spike depending on timing) — superposition makes it structural instead
        // of tuned: since every item's contribution is a pure function of its own elapsed time,
        // a second identical notch always contributes its own full distance regardless of what
        // else is queued, so draining both notches to completion must total 2x a single notch's
        // total, at every landing offset.
        var settings = PulseTestSettings();

        var isolated = new SmoothScrollEngine();
        isolated.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);
        long tIsolated = 0;
        var isolatedTotal = Math.Abs(RunToQuiet(isolated, settings, ref tIsolated));

        foreach (var landingOffsetMs in new long[] { 0, 20, 60, 120, 149, 200 })
        {
            var engine = new SmoothScrollEngine();
            engine.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);

            long t = 0;
            double combinedTotal = 0;
            while (t < landingOffsetMs)
            {
                t += 4;
                combinedTotal += engine.Tick(t, settings);
            }

            engine.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: t);
            combinedTotal += RunToQuiet(engine, settings, ref t);
            combinedTotal = Math.Abs(combinedTotal);

            Assert.InRange(combinedTotal, 2 * isolatedTotal - 4, 2 * isolatedTotal + 4);
        }
    }

    [Fact]
    public void Engine_superposition_total_for_n_notches_scales_linearly_regardless_of_cadence()
    {
        // Superposition: total emitted for N notches equals N times a single notch's total,
        // regardless of spacing — overlapping pulses simply sum instead of interfering.
        var settings = PulseTestSettings();

        var single = new SmoothScrollEngine();
        single.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);
        long tSingle = 0;
        var singleTotal = Math.Abs(RunToQuiet(single, settings, ref tSingle));

        foreach (var cadenceMs in new long[] { 20, 80, 150, 400 })
        {
            const int notches = 5;
            var engine = new SmoothScrollEngine();
            long t = 0;
            long total = 0;
            for (var i = 0; i < notches; i++)
            {
                engine.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: t);
                var nextPush = t + cadenceMs;
                while (t < nextPush)
                {
                    t += 4;
                    total += engine.Tick(t, settings);
                }
            }
            total += (long)RunToQuiet(engine, settings, ref t);

            var expected = notches * singleTotal;
            Assert.InRange(Math.Abs(total), expected - notches * 2, expected + notches * 2);
        }
    }

    [Fact]
    public void Engine_no_discontinuous_spike_when_a_notch_lands_mid_animation()
    {
        // No jump on resume: the tick right after a notch lands mid another notch's animation
        // must not spike — it should look like "leftover tail" + "a fresh pulse's own first
        // tick", never a multiple of a fresh gesture's own first-tick size.
        var settings = PulseTestSettings();

        var baseline = new SmoothScrollEngine();
        baseline.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);
        var baselineFirstTick = Math.Abs(baseline.Tick(4, settings));

        var engine = new SmoothScrollEngine();
        engine.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);
        long t = 0;
        int lastTick = 0;
        while (!engine.IsQuiet() && t < 100)
        {
            t += 4;
            lastTick = engine.Tick(t, settings);
        }
        Assert.False(engine.IsQuiet(), "test setup should catch the engine mid-animation, not fully settled");

        engine.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: t);
        var tickAfterLanding = Math.Abs(engine.Tick(t + 4, settings));

        // Expect roughly "old tail's own next contribution" + "a fresh pulse's first tick",
        // not a spike far beyond that sum.
        Assert.True(tickAfterLanding <= Math.Abs(lastTick) + baselineFirstTick + 2,
            $"tick after landing mid-animation ({tickAfterLanding}) should not exceed tail contribution ({Math.Abs(lastTick)}) + a fresh pulse's first tick ({baselineFirstTick})");
    }

    [Fact]
    public void Engine_dropped_or_coarse_ticks_self_correct_to_the_same_total()
    {
        // Dropped ticks self-correct: because progress is a function of real elapsed time (not
        // tick count), ticking at irregular/coarse intervals must deliver the same total as
        // ticking every 4 ms — a late or skipped timer callback (ScrollCoordinator's reentrancy
        // guard) must not lose motion.
        var settings = PulseTestSettings();

        var regular = new SmoothScrollEngine();
        regular.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);
        long tRegular = 0;
        var regularTotal = Math.Abs(RunToQuiet(regular, settings, ref tRegular));

        var coarse = new SmoothScrollEngine();
        coarse.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);
        double coarseTotal = 0;
        long tc = 0;
        // Skip several 4 ms ticks, then take one large 40 ms jump, repeatedly, until quiet.
        for (var i = 0; i < 200 && !coarse.IsQuiet(); i++)
        {
            tc += 40;
            coarseTotal += coarse.Tick(tc, settings);
        }

        Assert.InRange(Math.Abs(coarseTotal), regularTotal - 4, regularTotal + 4);
    }

    // ── AttackTimeMs ──────────────────────────────────────────────────────

    [Fact]
    public void AttackTimeMs_zero_matches_legacy_TailToHeadRatio_derived_behavior()
    {
        var settingsRatioOnly = PulseTestSettings();
        var settingsExplicitZero = PulseTestSettings();
        settingsExplicitZero.AttackTimeMs = 0;

        var engRatio = new SmoothScrollEngine();
        var engZero = new SmoothScrollEngine();
        engRatio.PushPhysicalDelta(120, settingsRatioOnly, accel: 1.0, nowMs: 0);
        engZero.PushPhysicalDelta(120, settingsExplicitZero, accel: 1.0, nowMs: 0);

        long t = 0;
        for (var i = 0; i < 200 && !(engRatio.IsQuiet() && engZero.IsQuiet()); i++)
        {
            t += 4;
            var a = engRatio.Tick(t, settingsRatioOnly);
            var b = engZero.Tick(t, settingsExplicitZero);
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void AttackTimeMs_controls_when_peak_per_tick_velocity_occurs()
    {
        var shortAttack = PulseTestSettings();
        shortAttack.AnimationTimeMs = 300;
        shortAttack.AttackTimeMs = 60; // scale 5

        var longAttack = PulseTestSettings();
        longAttack.AnimationTimeMs = 300;
        longAttack.AttackTimeMs = 150; // scale 2

        int PeakTickIndex(ScrollProfileSettings settings)
        {
            var engine = new SmoothScrollEngine();
            engine.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);

            long t = 0;
            var peakIndex = -1;
            var peakValue = double.MinValue;
            for (var i = 0; i < 200 && !engine.IsQuiet(); i++)
            {
                t += 4;
                var v = Math.Abs(engine.Tick(t, settings));
                if (v > peakValue)
                {
                    peakValue = v;
                    peakIndex = i;
                }
            }
            return peakIndex;
        }

        var shortPeak = PeakTickIndex(shortAttack);
        var longPeak = PeakTickIndex(longAttack);

        Assert.True(longPeak > shortPeak,
            $"a longer attack ({longAttack.AttackTimeMs} ms) should peak substantially later than a shorter one ({shortAttack.AttackTimeMs} ms): short peak tick={shortPeak}, long peak tick={longPeak}");
    }

    [Fact]
    public void AttackTimeMs_preserves_consistency_property_when_set()
    {
        var settings = PulseTestSettings();
        settings.AttackTimeMs = 60;

        var isolated = new SmoothScrollEngine();
        isolated.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);
        long tIsolated = 0;
        var isolatedTotal = Math.Abs(RunToQuiet(isolated, settings, ref tIsolated));

        foreach (var landingOffsetMs in new long[] { 0, 20, 60, 120, 149, 200 })
        {
            var engine = new SmoothScrollEngine();
            engine.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);

            long t = 0;
            double combinedTotal = 0;
            while (t < landingOffsetMs)
            {
                t += 4;
                combinedTotal += engine.Tick(t, settings);
            }

            engine.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: t);
            combinedTotal += RunToQuiet(engine, settings, ref t);
            combinedTotal = Math.Abs(combinedTotal);

            Assert.InRange(combinedTotal, 2 * isolatedTotal - 4, 2 * isolatedTotal + 4);
        }
    }

    [Fact]
    public void AttackTimeMs_larger_than_AnimationTimeMs_still_completes_monotonically_and_in_full()
    {
        var settings = PulseTestSettings();
        settings.AnimationTimeMs = 150;
        settings.AttackTimeMs = 5000; // clamped to animationTimeMs -> scale floored at 1

        var engine = new SmoothScrollEngine();
        engine.PushPhysicalDelta(120, settings, accel: 1.0, nowMs: 0);

        long t = 0;
        double total = 0;
        double prevAbsTotal = 0;
        for (var i = 0; i < 500 && !engine.IsQuiet(); i++)
        {
            t += 4;
            total += engine.Tick(t, settings);
            var absTotal = Math.Abs(total);
            Assert.True(absTotal >= prevAbsTotal - 1e-6, "cumulative emitted distance should be monotone");
            prevAbsTotal = absTotal;
        }

        Assert.True(engine.IsQuiet());
        // StepSizePx=80 (from PulseTestSettings) -> StepScale=4 -> 120 * 4 = 480 total units.
        Assert.InRange(Math.Abs(total), 476, 484);
    }

    [Fact]
    public void Pulse_function_shape_is_normalized_monotonic_and_starts_at_zero_velocity()
    {
        const double scale = 4.0; // 1 + TailToHeadRatio(3)

        Assert.Equal(0.0, SmoothScrollEngine.Pulse(0, scale), 10);
        Assert.Equal(1.0, SmoothScrollEngine.Pulse(1, scale), 10);

        double prev = -1;
        for (var t = 0.0; t <= 1.0; t += 0.01)
        {
            var v = SmoothScrollEngine.Pulse(t, scale);
            Assert.True(v >= prev - 1e-9, $"Pulse should be monotonically increasing (t={t}, v={v}, prev={prev})");
            prev = v;
        }

        // Derivative near t=0 is ~0 — sampled numerically. This is precisely what prevents a
        // fresh pulse from producing an abrupt first-tick jump.
        const double h = 1e-4;
        var derivativeNearZero = (SmoothScrollEngine.Pulse(h, scale) - SmoothScrollEngine.Pulse(0, scale)) / h;
        Assert.True(derivativeNearZero < 0.01, $"derivative near t=0 should be ~0, got {derivativeNearZero:F5}");
    }
}
