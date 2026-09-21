using System.Diagnostics;

namespace SmoothMice.Core.Diagnostics;

/// <summary>Runtime policy is intentionally independent from the module visibility preference.</summary>
public enum FreeSpinDetectionMode { ReadOnly, Suppress }
public enum FreeSpinVerdict { Pass, InertiaCandidate, Abstain }
public enum FreeSpinInertiaPhase { None, Lift, Landing, Reposition }

public sealed class FreeSpinWheelContext
{
    // [hasPriorWheel, priorIntervalMs, priorAbsDelta, currentAbsDelta, netMovePx,
    //  movePathPx, moveSpeedPxPerSecond, latestMoveAgeMs]
    public double[] Values { get; set; } = Array.Empty<double>();
    public bool HasButtonsDown { get; set; }
    public bool IsHorizontal { get; set; }
}

public sealed class FreeSpinDetectionDecision
{
    public FreeSpinVerdict Verdict { get; set; }
    public double RawScore { get; set; }
    public double ConservativeConfidence { get; set; }
    public bool ShouldSuppress { get; set; }
    public FreeSpinInertiaPhase LikelyPhase { get; set; }
    public string Reason { get; set; } = "Detector unavailable; event passed.";
}

/// <summary>
/// Bounded causal context. It never looks at a future event and deliberately abstains while a
/// button is down, because the calibration data does not establish that as safe to suppress.
/// </summary>
public sealed class FreeSpinCausalHistory
{
    private const int MaximumMoves = 24;
    private readonly Queue<FreeSpinCalibrationEvent> _moves = new();
    private int _buttonMask;
    private long _lastWheelTicks;
    private int _lastWheelDelta;

    public void Reset()
    {
        _moves.Clear();
        _buttonMask = 0;
        _lastWheelTicks = 0;
        _lastWheelDelta = 0;
    }

    public void Observe(FreeSpinRawInputEvent input)
    {
        if (input.Kind == FreeSpinRawEventKind.Button && input.Button != FreeSpinMouseButton.None)
        {
            var bit = 1 << ((int)input.Button - 1);
            if (input.IsButtonDown) _buttonMask |= bit; else _buttonMask &= ~bit;
        }
        else if (input.Kind == FreeSpinRawEventKind.Move)
        {
            _moves.Enqueue(new FreeSpinCalibrationEvent { StopwatchTicks = input.StopwatchTicks, X = input.X, Y = input.Y });
            while (_moves.Count > MaximumMoves) _moves.Dequeue();
        }
    }

    public FreeSpinWheelContext? CreateForWheel(long ticks, int delta, bool horizontal)
    {
        if (delta == 0 || _buttonMask != 0)
            return null;
        var hasPriorWheel = _lastWheelTicks != 0;
        var elapsedMs = hasPriorWheel ? (ticks - _lastWheelTicks) * 1000d / Stopwatch.Frequency : 0d;
        // A first pulse has no preceding wheel but is still a causally valid context. Its
        // explicit flag/sentinel lets the model learn it without pretending a prior pulse.
        if (hasPriorWheel && (elapsedMs < 0 || elapsedMs > 2_000))
            return null;
        var netMovement = 0d;
        var pathMovement = 0d;
        var speed = 0d;
        var latestAgeMs = 2_000d;
        if (_moves.Count > 1)
        {
            var a = _moves.Peek();
            var b = _moves.Last();
            netMovement = Math.Sqrt((b.X - a.X) * (double)(b.X - a.X) + (b.Y - a.Y) * (double)(b.Y - a.Y));
            var previous = a;
            foreach (var move in _moves.Skip(1))
            {
                pathMovement += Math.Sqrt((move.X - previous.X) * (double)(move.X - previous.X) + (move.Y - previous.Y) * (double)(move.Y - previous.Y));
                previous = move;
            }
            var spanSeconds = Math.Max(.001, (b.StopwatchTicks - a.StopwatchTicks) / (double)Stopwatch.Frequency);
            speed = pathMovement / spanSeconds;
            latestAgeMs = Math.Max(0, Math.Min(2_000, (ticks - b.StopwatchTicks) * 1000d / Stopwatch.Frequency));
        }
        else if (_moves.Count == 1)
        {
            latestAgeMs = Math.Max(0, Math.Min(2_000, (ticks - _moves.Peek().StopwatchTicks) * 1000d / Stopwatch.Frequency));
        }
        return new FreeSpinWheelContext
        {
            Values = [hasPriorWheel ? 1d : 0d, elapsedMs, Math.Abs(_lastWheelDelta), Math.Abs(delta), netMovement, pathMovement, speed, latestAgeMs],
            HasButtonsDown = false,
            IsHorizontal = horizontal,
        };
    }

    public void CommitWheel(long ticks, int delta) { _lastWheelTicks = ticks; _lastWheelDelta = delta; }

    public static IEnumerable<FreeSpinWheelContext> Extract(FreeSpinCalibrationSample sample)
    {
        var history = new FreeSpinCausalHistory();
        foreach (var item in sample.Events.OrderBy(e => e.StopwatchTicks))
        {
            if (item.Kind == "wheel")
            {
                var context = history.CreateForWheel(item.StopwatchTicks, item.WheelDelta, item.WheelAxis == "horizontal");
                if (context is not null) yield return context;
                history.CommitWheel(item.StopwatchTicks, item.WheelDelta);
                continue;
            }
            var raw = new FreeSpinRawInputEvent { StopwatchTicks = item.StopwatchTicks, X = item.X, Y = item.Y };
            if (item.Kind == "move") raw.Kind = FreeSpinRawEventKind.Move;
            else if (item.Kind == "button")
            {
                raw.Kind = FreeSpinRawEventKind.Button;
                raw.IsButtonDown = item.ButtonTransition?.EndsWith("Down", StringComparison.Ordinal) == true;
                raw.Button = ParseButton(item.ButtonTransition);
            }
            else continue;
            history.Observe(raw);
        }
    }

    private static FreeSpinMouseButton ParseButton(string? transition) => transition?.StartsWith("left", StringComparison.OrdinalIgnoreCase) == true ? FreeSpinMouseButton.Left :
        transition?.StartsWith("right", StringComparison.OrdinalIgnoreCase) == true ? FreeSpinMouseButton.Right :
        transition?.StartsWith("middle", StringComparison.OrdinalIgnoreCase) == true ? FreeSpinMouseButton.Middle :
        transition?.StartsWith("x1", StringComparison.OrdinalIgnoreCase) == true ? FreeSpinMouseButton.X1 :
        transition?.StartsWith("x2", StringComparison.OrdinalIgnoreCase) == true ? FreeSpinMouseButton.X2 : FreeSpinMouseButton.None;
}

public sealed class FreeSpinTrainingPoint
{
    public string GroupId { get; set; } = string.Empty;
    public bool Positive { get; set; }
    public FreeSpinInertiaPhase InertiaPhase { get; set; }
    public double[] Values { get; set; } = Array.Empty<double>();
    public double Weight { get; set; }
    public double BaseWeight { get; set; }
    public bool Horizontal { get; set; }
}

public sealed class FreeSpinCalibrationCutoff
{
    public double ScoreCutoff { get; set; }
    public double ConservativeConfidence { get; set; }
    public int TruePositiveGroups { get; set; }
    public int LegitimateFalsePositiveGroups { get; set; }
    public int LegitimateCalibrationGroups { get; set; }
}

/// <summary>Immutable, bounded kNN snapshot used directly by the mouse hook.</summary>
public sealed class FreeSpinDetectionModel
{
    private readonly List<FreeSpinTrainingPoint> _points;
    private readonly double[] _median;
    private readonly double[] _mad;
    private readonly List<FreeSpinCalibrationCutoff> _cutoffs;
    private readonly double _inDistributionDistance;
    private readonly int _positiveGroups;
    private readonly int _legitimateGroups;
    public const int MinimumLegitimateCalibrationGroups = 5;

    private FreeSpinDetectionModel(List<FreeSpinTrainingPoint> points, double[] median, double[] mad,
        List<FreeSpinCalibrationCutoff> cutoffs, double inDistributionDistance, int positiveGroups, int legitimateGroups)
    { _points = points; _median = median; _mad = mad; _cutoffs = cutoffs; _inDistributionDistance = inDistributionDistance; _positiveGroups = positiveGroups; _legitimateGroups = legitimateGroups; }

    public bool IsReady => _points.Count > 0 && _positiveGroups >= 2 && _legitimateGroups >= MinimumLegitimateCalibrationGroups && _cutoffs.Count > 0;
    public int LegitimateCalibrationGroupCount => _legitimateGroups;
    public double InDistributionDistance => _inDistributionDistance;
    public double MaximumSupportedConfidence => _cutoffs.Count == 0 ? 0 : _cutoffs.Max(c => c.ConservativeConfidence);
    public string Summary => !IsReady ? $"Insufficient independent calibration samples (need {MinimumLegitimateCalibrationGroups} legitimate captures); all events pass." :
        $"Ready: {_positiveGroups} inertia captures, {_legitimateGroups} legitimate captures, {_points.Count} balanced wheel contexts.";
    public IReadOnlyList<FreeSpinCalibrationCutoff> Cutoffs => _cutoffs;

    /// <summary>Diagnostic invariant: live OOD checks use the model's final global MAD scale.</summary>
    public bool IsInDistribution(FreeSpinWheelContext context)
    {
        if (context is null || context.Values.Length != _median.Length) return false;
        var nearest = FindNearest(context, _points);
        return nearest.Count != 0 && Distance(context.Values, nearest[0].Values, _median, _mad) <= _inDistributionDistance;
    }

    public static FreeSpinDetectionModel Train(IEnumerable<FreeSpinCalibrationSample> samples)
    {
        var points = new List<FreeSpinTrainingPoint>();
        foreach (var sample in samples.Where(s => FreeSpinCalibrationValidation.TryValidate(s, out _)))
        {
            var inertiaPhase = ParseInertiaPhase(sample.Phase);
            var positive = inertiaPhase != FreeSpinInertiaPhase.None;
            var contexts = FreeSpinCausalHistory.Extract(sample).Take(64).ToList();
            if (contexts.Count == 0) continue;
            foreach (var context in contexts)
                points.Add(new FreeSpinTrainingPoint { GroupId = sample.Id, Positive = positive, InertiaPhase = inertiaPhase, Values = context.Values, Horizontal = context.IsHorizontal, BaseWeight = 1d / contexts.Count, Weight = 1d / contexts.Count });
        }
        if (points.Count == 0) return new FreeSpinDetectionModel(points, Array.Empty<double>(), Array.Empty<double>(), [], 0, 0, 0);
        BalanceClasses(points);
        var median = RobustMedian(points.Select(p => p.Values).ToList());
        var mad = RobustMad(points.Select(p => p.Values).ToList(), median);
        var cutoffs = BuildGroupedOof(points, median, mad, out var oofDistance);
        return new FreeSpinDetectionModel(points, median, mad, cutoffs, Math.Max(oofDistance, .001), points.Where(p => p.Positive).Select(p => p.GroupId).Distinct().Count(), points.Where(p => !p.Positive).Select(p => p.GroupId).Distinct().Count());
    }

    public FreeSpinDetectionDecision Evaluate(FreeSpinWheelContext? context, FreeSpinDetectionMode mode, int thresholdPercent)
    {
        try
        {
            if (!IsReady) return new FreeSpinDetectionDecision { Verdict = FreeSpinVerdict.Abstain, Reason = Summary };
            if (context is null || context.HasButtonsDown || context.Values.Length != _median.Length)
                return new FreeSpinDetectionDecision { Verdict = FreeSpinVerdict.Abstain, Reason = "Missing context or button held; event passed." };
            var nearest = FindNearest(context, _points);
            if (nearest.Count == 0) return new FreeSpinDetectionDecision { Verdict = FreeSpinVerdict.Abstain, Reason = "Input context is not represented by calibration; event passed." };
            var nearestDistance = Distance(context.Values, nearest[0].Values, _median, _mad);
            if (nearestDistance > _inDistributionDistance)
                return new FreeSpinDetectionDecision { Verdict = FreeSpinVerdict.Abstain, Reason = "Out-of-distribution wheel context; event passed." };
            var pos = 0d; var total = 0d;
            foreach (var point in nearest)
            {
                var weight = point.Weight / Math.Max(.01, Distance(context.Values, point.Values, _median, _mad));
                total += weight; if (point.Positive) pos += weight;
            }
            var score = total == 0 ? 0 : pos / total;
            var cutoff = _cutoffs.Where(c => c.ScoreCutoff <= score).LastOrDefault();
            var confidence = cutoff?.ConservativeConfidence ?? 0;
            var phaseSupport = nearest.Where(p => p.Positive)
                .GroupBy(p => p.InertiaPhase)
                .Select(g => new { Phase = g.Key, Weight = g.Sum(p => p.Weight), Groups = g.Select(p => p.GroupId).Distinct().Count() })
                .OrderByDescending(x => x.Weight).FirstOrDefault();
            var likelyPhase = phaseSupport is { Groups: >= 2 } ? phaseSupport.Phase : FreeSpinInertiaPhase.None;
            var eligible = cutoff is not null && cutoff.LegitimateCalibrationGroups >= MinimumLegitimateCalibrationGroups && cutoff.LegitimateFalsePositiveGroups == 0 && likelyPhase != FreeSpinInertiaPhase.None && confidence * 100 >= thresholdPercent;
            return new FreeSpinDetectionDecision
            {
                Verdict = score >= .5 && likelyPhase != FreeSpinInertiaPhase.None ? FreeSpinVerdict.InertiaCandidate : score >= .5 ? FreeSpinVerdict.Abstain : FreeSpinVerdict.Pass,
                RawScore = score, ConservativeConfidence = confidence,
                ShouldSuppress = mode == FreeSpinDetectionMode.Suppress && eligible,
                LikelyPhase = likelyPhase,
                Reason = eligible ? $"High-confidence calibrated {PhaseDisplay(likelyPhase)} inertia context." : cutoff is null ? "Score has no calibrated safe cutoff; event passed." :
                    cutoff.LegitimateCalibrationGroups < MinimumLegitimateCalibrationGroups ? "Too few independent legitimate captures support suppression; event passed." :
                    cutoff.LegitimateFalsePositiveGroups != 0 ? "Observed legitimate false positives at this cutoff; event passed." :
                    likelyPhase == FreeSpinInertiaPhase.None ? "Fewer than two independent captures support one inertia phase; event passed." :
                    "Conservative suppression confidence is below the configured threshold; event passed.",
            };
        }
        catch { return new FreeSpinDetectionDecision { Verdict = FreeSpinVerdict.Abstain, Reason = "Detector error; event passed." }; }
    }

    public static double BetaLowerBound(int truePositive, int falsePositive) => BetaQuantile(.05, truePositive + .5, falsePositive + .5);
    public static double BetaQuantile(double probability, double a, double b)
    {
        var low = 0d; var high = 1d;
        for (var i = 0; i < 70; i++) { var x = (low + high) / 2; if (RegularizedBeta(x, a, b) < probability) low = x; else high = x; }
        return (low + high) / 2;
    }

    public static double[] RobustMedian(IReadOnlyList<double[]> vectors) => Enumerable.Range(0, vectors[0].Length).Select(i => Median(vectors.Select(v => v[i]))).ToArray();
    public static double[] RobustMad(IReadOnlyList<double[]> vectors, double[] median) => Enumerable.Range(0, median.Length).Select(i => Math.Max(.001, Median(vectors.Select(v => Math.Abs(v[i] - median[i]))))).ToArray();
    /// <summary>
    /// Increasing the score cutoff must never make the reported evidence weaker. A suffix-min
    /// is conservative: every corrected bound remains no greater than its own local Jeffreys
    /// bound, unlike a prefix-max which can borrow optimistic evidence from a looser cutoff.
    /// </summary>
    public static double[] ConservativeMonotonicBounds(IReadOnlyList<double> raw)
    {
        var corrected = raw.ToArray();
        for (var i = corrected.Length - 2; i >= 0; i--)
            corrected[i] = Math.Min(corrected[i], corrected[i + 1]);
        return corrected;
    }

    private static void BalanceClasses(List<FreeSpinTrainingPoint> points)
    {
        foreach (var point in points) point.Weight = point.BaseWeight;
        var positive = points.Where(p => p.Positive).Sum(p => p.BaseWeight); var negative = points.Where(p => !p.Positive).Sum(p => p.BaseWeight);
        if (positive == 0 || negative == 0) return;
        foreach (var point in points) point.Weight *= point.Positive ? .5 / positive : .5 / negative;
    }
    private static List<FreeSpinCalibrationCutoff> BuildGroupedOof(List<FreeSpinTrainingPoint> all, double[] globalMedian, double[] globalMad, out double oofDistance)
    {
        var predictions = new List<(string Group, bool Positive, double Score)>();
        var oofDistances = new List<double>();
        foreach (var group in all.Select(p => p.GroupId).Distinct())
        {
            var train = all.Where(p => p.GroupId != group).Select(p => new FreeSpinTrainingPoint { GroupId = p.GroupId, Positive = p.Positive, InertiaPhase = p.InertiaPhase, Values = p.Values, Horizontal = p.Horizontal, BaseWeight = p.BaseWeight, Weight = p.BaseWeight }).ToList();
            if (train.Count == 0) continue;
            // Fit scaling inside each held-out group, not on the held-out sample: no capture
            // contributes directly or indirectly to the score used to calibrate its cutoff.
            var trainMedian = RobustMedian(train.Select(p => p.Values).ToList());
            var trainMad = RobustMad(train.Select(p => p.Values).ToList(), trainMedian);
            BalanceClasses(train);
            foreach (var point in all.Where(p => p.GroupId == group))
            {
                var near = train.Where(p => p.Horizontal == point.Horizontal).OrderBy(p => Distance(point.Values, p.Values, trainMedian, trainMad)).Take(9).ToList();
                if (near.Count == 0) continue;
                // OOD uses the same final global scale as live Evaluate, but its nearest
                // neighbour remains outside the held-out capture group.
                oofDistances.Add(Distance(point.Values, near[0].Values, globalMedian, globalMad));
                var weighted = near.Sum(p => p.Weight / Math.Max(.01, Distance(point.Values, p.Values, trainMedian, trainMad)));
                var pos = near.Where(p => p.Positive).Sum(p => p.Weight / Math.Max(.01, Distance(point.Values, p.Values, trainMedian, trainMad)));
                predictions.Add((group, point.Positive, weighted == 0 ? 0 : pos / weighted));
            }
        }
        // Exact OOF score thresholds preserve every empirically observed transition. A zero
        // lower boundary documents the all-context case; no arbitrary .05 grid can hide a
        // safe region near one or borrow confidence from a neighbouring threshold.
        var exactCutoffs = predictions.Select(p => p.Score).Append(0d).Distinct().OrderBy(score => score).ToArray();
        var rawBounds = new List<double>(exactCutoffs.Length);
        var result = new List<FreeSpinCalibrationCutoff>(exactCutoffs.Length);
        var legitimateGroups = all.Where(p => !p.Positive).Select(p => p.GroupId).Distinct().Count();
        foreach (var cutoff in exactCutoffs)
        {
            var groups = predictions.GroupBy(p => new { p.Group, p.Positive }).Select(g => new { g.Key.Positive, Hit = g.Any(p => p.Score >= cutoff) }).ToList();
            var tp = groups.Count(g => g.Positive && g.Hit); var fp = groups.Count(g => !g.Positive && g.Hit);
            rawBounds.Add(BetaLowerBound(tp, fp));
            result.Add(new FreeSpinCalibrationCutoff { ScoreCutoff = cutoff, TruePositiveGroups = tp, LegitimateFalsePositiveGroups = fp, LegitimateCalibrationGroups = legitimateGroups });
        }
        var correctedBounds = ConservativeMonotonicBounds(rawBounds);
        for (var i = 0; i < result.Count; i++) result[i].ConservativeConfidence = correctedBounds[i];
        oofDistance = Percentile(oofDistances, .95);
        return result;
    }
    private static FreeSpinInertiaPhase ParseInertiaPhase(string phase) => phase switch
    {
        "lift" => FreeSpinInertiaPhase.Lift,
        "landing" => FreeSpinInertiaPhase.Landing,
        "reposition" => FreeSpinInertiaPhase.Reposition,
        _ => FreeSpinInertiaPhase.None,
    };
    private static string PhaseDisplay(FreeSpinInertiaPhase phase) => phase switch
    {
        FreeSpinInertiaPhase.Lift => "lift",
        FreeSpinInertiaPhase.Landing => "landing",
        FreeSpinInertiaPhase.Reposition => "reposition",
        _ => "unknown",
    };
    /// <summary>Fixed-size insertion selection: inference does no sorting or unbounded allocation.</summary>
    private List<FreeSpinTrainingPoint> FindNearest(FreeSpinWheelContext context, List<FreeSpinTrainingPoint> points)
    {
        var closest = new List<(FreeSpinTrainingPoint Point, double Distance)>(9);
        foreach (var point in points)
        {
            if (point.Horizontal != context.IsHorizontal) continue;
            var distance = Distance(context.Values, point.Values, _median, _mad);
            var index = 0;
            while (index < closest.Count && closest[index].Distance <= distance) index++;
            if (index >= 9) continue;
            closest.Insert(index, (point, distance));
            if (closest.Count > 9) closest.RemoveAt(9);
        }
        return closest.Select(item => item.Point).ToList();
    }
    private static double Distance(double[] a, double[] b, double[] median, double[] mad) { var sum = 0d; for (var i = 0; i < a.Length; i++) { var d = (a[i] - b[i]) / mad[i]; sum += d * d; } return Math.Sqrt(sum); }
    private static double Median(IEnumerable<double> values) { var a = values.OrderBy(v => v).ToArray(); return a.Length == 0 ? 0 : a[a.Length / 2]; }
    private static double Percentile(List<double> values, double fraction) { var a = values.OrderBy(v => v).ToArray(); return a.Length == 0 ? 0 : a[Math.Min(a.Length - 1, (int)Math.Floor((a.Length - 1) * fraction))]; }
    private static double RegularizedBeta(double x, double a, double b)
    {
        if (x <= 0) return 0; if (x >= 1) return 1;
        var bt = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
        return x < (a + 1) / (a + b + 2) ? bt * BetaFraction(x, a, b) / a : 1 - bt * BetaFraction(1 - x, b, a) / b;
    }
    private static double BetaFraction(double x, double a, double b)
    {
        const int max = 100; const double eps = 3e-12; const double tiny = 1e-30;
        var qab = a + b; var qap = a + 1; var qam = a - 1; var c = 1d; var d = 1 - qab * x / qap; if (Math.Abs(d) < tiny) d = tiny; d = 1 / d; var h = d;
        for (var m = 1; m <= max; m++) { var m2 = 2 * m; var aa = m * (b - m) * x / ((qam + m2) * (a + m2)); d = 1 + aa * d; if (Math.Abs(d) < tiny) d = tiny; c = 1 + aa / c; if (Math.Abs(c) < tiny) c = tiny; d = 1 / d; h *= d * c; aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2)); d = 1 + aa * d; if (Math.Abs(d) < tiny) d = tiny; c = 1 + aa / c; if (Math.Abs(c) < tiny) c = tiny; d = 1 / d; var delta = d * c; h *= delta; if (Math.Abs(delta - 1) < eps) break; }
        return h;
    }
    private static double LogGamma(double z) { var x = 0.99999999999980993; var c = new[] { 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 }; if (z < .5) return Math.Log(Math.PI) - Math.Log(Math.Sin(Math.PI * z)) - LogGamma(1 - z); z -= 1; for (var i = 0; i < c.Length; i++) x += c[i] / (z + i + 1); var t = z + c.Length - .5; return .5 * Math.Log(2 * Math.PI) + (z + .5) * Math.Log(t) - t + Math.Log(x); }
}
