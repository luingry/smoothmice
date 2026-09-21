using SmoothMice.Core.Config;

namespace SmoothMice.Core.Scrolling;

/// <summary>
/// Pulse-queue smooth scroll engine — a port of Balazs Galambosi's MIT-licensed SmoothScroll
/// (smoothscroll.js) model, whose easing curve is credited to Michael Herf ("Stopping",
/// stereopsis.com). Re-implemented from the public algorithm description, not decompiled.
///
/// Every physical wheel notch becomes its own independent queue item with its own start
/// timestamp and total distance. Each tick, every item computes its progress from REAL elapsed
/// time independently, and their contributions are SUMMED (superposition). This replaces an
/// earlier single-shared-state model (one scalar "remaining" plus one persistent "speed" ramp)
/// that was structurally history-dependent: what a new notch emitted on its first tick depended
/// on the ramp state left behind by whatever had been animating before it, causing visible
/// jumps/spikes when a notch landed mid-animation. A pulse queue is linear and time-invariant —
/// each item always delivers exactly its own distance over exactly its own duration, regardless
/// of what else is in flight, so there is nothing for a new item to inherit.
///
/// There must be NO "merge", "nudge", "reset speed", or "negligible tail" heuristics here — that
/// entire class of bug is what superposition is designed to make structurally impossible, not
/// something to re-approximate with a threshold.
/// </summary>
public sealed class SmoothScrollEngine
{
    // A FreeSpin wheel can report physical pulses much faster than Windows can render them.
    // Keep enough motion for a very fast continuous scroll, but never let a pathological burst
    // turn into an unbounded post-scroll backlog or an out-of-range injection delta.
    public const int MaximumPendingDeltaUnits = 48_000;
    public const int MaximumDeltaPerTick = 1_920;

    // Bound on queued pulses. A FreeSpin burst can enqueue far faster than the 4 ms tick can
    // drain; each item is nearly weightless once distance/emitted is small, so when full we fold
    // the OLDEST item's un-emitted remainder into the next-oldest item's distance and drop it —
    // both are old, both are nearly spent, and the resulting single-tick contribution error is
    // negligible relative to MaximumDeltaPerTick.
    private const int MaximumQueueLength = 256;

    private readonly List<PulseItem> _queue = new();

    // Reused across ticks. Tick runs on the 4 ms timer thread in a latency-sensitive input path,
    // so it must not allocate; the queue can never exceed MaximumQueueLength, so one fixed buffer
    // covers every tick.
    private readonly double[] _tickTargets = new double[MaximumQueueLength];

    private double _fracAccum; // fractional carry for integer output, accumulated once per tick

    private struct PulseItem
    {
        public double Distance; // total signed units this pulse will deliver
        public long StartMs;
        public double Emitted; // signed units already delivered (== Distance when t >= 1)
    }

    public void Reset()
    {
        _queue.Clear();
        _fracAccum = 0;
    }

    /// <summary>Feed a physical wheel delta. Enqueues an independent pulse item.</summary>
    public void PushPhysicalDelta(int rawDelta, ScrollProfileSettings settings, double accel, long nowMs)
    {
        if (rawDelta == 0) return;

        var units = rawDelta * ScrollMath.StepScale(settings.StepSizePx) * accel;
        if (double.IsNaN(units) || double.IsInfinity(units))
            return;

        var pendingSign = PendingSign();
        if (pendingSign != 0 && Math.Sign(units) != pendingSign)
        {
            // Direction reversal: discard all queued motion and start fresh — matches the
            // previous engine's behavior and SmoothScroll's own directionCheck.
            _queue.Clear();
            _fracAccum = 0;
        }

        var pending = PendingDistance();
        var budget = MaximumPendingDeltaUnits - pending;
        if (budget <= 0)
            return; // already at the pending cap; drop this push rather than overflow it

        if (Math.Abs(units) > budget)
            units = budget * Math.Sign(units);

        if (_queue.Count >= MaximumQueueLength)
        {
            // Fold the oldest (nearly-spent) item's remaining distance into the next-oldest item
            // so total pending distance is preserved, then drop the oldest slot to make room.
            var oldest = _queue[0];
            var oldestRemaining = oldest.Distance - oldest.Emitted;
            var next = _queue[1];
            next.Distance += oldestRemaining;
            _queue[1] = next;
            _queue.RemoveAt(0);
        }

        _queue.Add(new PulseItem { Distance = units, StartMs = nowMs, Emitted = 0.0 });
    }

    /// <summary>Advance animation by one tick using real elapsed time; returns signed wheel-delta units to inject.</summary>
    public int Tick(long nowMs, ScrollProfileSettings settings)
    {
        if (_queue.Count == 0) return 0;

        var animationTimeMs = Math.Max(1, settings.AnimationTimeMs);
        // Acceleration phase occupies t < 1/scale, i.e. real duration animationTimeMs/scale, so an
        // explicit attack time is just the inverse: scale = animationTime/attack. Attack is capped
        // at animationTimeMs so scale never drops below 1 — below 1 the curve would be truncated
        // mid-acceleration and end at peak velocity instead of easing in.
        var pulseScale = settings.AttackTimeMs > 0
            ? Math.Max(1.0, animationTimeMs / (double)Math.Min(settings.AttackTimeMs, animationTimeMs))
            : 1.0 + Math.Min(Math.Max(settings.TailToHeadRatio, 0.5), 12.0);
        var useEasing = settings.AnimationEasing;

        // First pass: each item's contribution is purely a function of real elapsed time,
        // independent of every other item (superposition) — compute the ideal, uncapped
        // per-item contribution and sum it.
        var count = _queue.Count;
        var idealTargets = _tickTargets;
        double rawSum = 0;
        for (var i = 0; i < count; i++)
        {
            var item = _queue[i];
            var t = (nowMs - item.StartMs) / (double)animationTimeMs;
            if (t < 0) t = 0;

            var target = t >= 1.0
                ? item.Distance // guarantee the full distance is eventually delivered, no drift
                : item.Distance * (useEasing ? Pulse(t, pulseScale) : t);

            idealTargets[i] = target;
            rawSum += target - item.Emitted;
        }

        // Cap the summed per-tick output. Scale every item's ACTUAL contribution down by the same
        // factor rather than discarding the excess — each item's Emitted then lags behind its
        // ideal time-based target, so the shortfall simply reappears as that item's contribution
        // on a later tick (it isn't removed until Emitted actually reaches Distance). This is the
        // FreeSpin safety bound; it does not apply on ordinary, unsaturated ticks (scale == 1).
        var appliedSum = Clamp(rawSum, -MaximumDeltaPerTick, MaximumDeltaPerTick);
        var scale = rawSum == 0 ? 0 : appliedSum / rawSum;

        for (var i = count - 1; i >= 0; i--)
        {
            var item = _queue[i];
            item.Emitted += (idealTargets[i] - item.Emitted) * scale;

            if (Math.Abs(item.Distance - item.Emitted) < 1e-6)
                _queue.RemoveAt(i);
            else
                _queue[i] = item;
        }

        _fracAccum += appliedSum;
        var whole = (int)_fracAccum;
        _fracAccum -= whole;
        return whole;
    }

    public bool IsQuiet() => _queue.Count == 0;

    /// <summary>
    /// Raw, unnormalized Michael Herf "pulse" easing (see stereopsis.com, "Stopping"). C¹
    /// continuous with zero derivative at x=0 — every pulse starts at zero velocity, which is
    /// what makes superposed pulses jump-free at their own onset. Below x=1/scale it's an
    /// acceleration ramp; above, an exponential "viscous drag" decay tail. Higher scale = shorter
    /// acceleration phase, longer tail.
    /// </summary>
    public static double PulseRaw(double x, double scale)
    {
        x *= scale;
        if (x < 1.0)
            return x - (1.0 - Math.Exp(-x));

        var start = Math.Exp(-1.0);
        x -= 1.0;
        return start + (1.0 - Math.Exp(-x)) * (1.0 - start);
    }

    /// <summary>Normalized so Pulse(1) == 1 exactly. <paramref name="t"/> is progress in [0, 1].</summary>
    public static double Pulse(double t, double scale)
    {
        if (t <= 0) return 0.0;
        if (t >= 1) return 1.0;
        return PulseRaw(t, scale) / PulseRaw(1.0, scale);
    }

    private int PendingSign()
    {
        double signedSum = 0;
        foreach (var item in _queue)
            signedSum += item.Distance - item.Emitted;
        return Math.Sign(signedSum);
    }

    private double PendingDistance()
    {
        double sum = 0;
        foreach (var item in _queue)
            sum += Math.Abs(item.Distance - item.Emitted);
        return sum;
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}
