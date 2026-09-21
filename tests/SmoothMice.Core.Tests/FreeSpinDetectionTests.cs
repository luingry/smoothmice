using SmoothMice.Core.Diagnostics;
using SmoothMice.Core.Profiles;
using SmoothMice.Core.Config;
using SmoothMice.Infrastructure.Windows;
using System.Reflection;
using Xunit;

namespace SmoothMice.Core.Tests;

public sealed class FreeSpinDetectionTests
{
    [Fact]
    public void CausalHistory_RepresentsTheFirstWheelWithAnExplicitNoPriorFeature()
    {
        var history = new FreeSpinCausalHistory();
        var first = history.CreateForWheel(1_000, 120, false);
        Assert.NotNull(first);
        Assert.Equal(0, first!.Values[0]);
        history.CommitWheel(1_000, 120);
        var context = history.CreateForWheel(1_000 + System.Diagnostics.Stopwatch.Frequency / 10, 120, false);
        Assert.NotNull(context);
        Assert.Equal(8, context!.Values.Length);
        Assert.Equal(1, context.Values[0]);
        Assert.InRange(context.Values[1], 99, 101);
    }

    [Fact]
    public void RobustScaling_UsesMedianAndNonZeroMad()
    {
        var values = new List<double[]> { new[] { 1d, 10d }, new[] { 2d, 10d }, new[] { 100d, 10d } };
        var median = FreeSpinDetectionModel.RobustMedian(values);
        var mad = FreeSpinDetectionModel.RobustMad(values, median);
        Assert.Equal(2, median[0]);
        Assert.Equal(10, median[1]);
        Assert.True(mad[0] > 0);
        Assert.True(mad[1] > 0);
    }

    [Fact]
    public void CausalHistory_UsesOnlyPriorMovementForPathSpeedAndRecency()
    {
        var history = new FreeSpinCausalHistory();
        history.Observe(new FreeSpinRawInputEvent { Kind = FreeSpinRawEventKind.Move, StopwatchTicks = 1_000, X = 0, Y = 0 });
        history.Observe(new FreeSpinRawInputEvent { Kind = FreeSpinRawEventKind.Move, StopwatchTicks = 1_000 + System.Diagnostics.Stopwatch.Frequency / 10, X = 3, Y = 4 });
        var context = history.CreateForWheel(1_000 + System.Diagnostics.Stopwatch.Frequency / 5, 120, false)!;
        Assert.Equal(5, context.Values[4]);
        Assert.Equal(5, context.Values[5]);
        Assert.InRange(context.Values[6], 49, 51);
        Assert.InRange(context.Values[7], 99, 101);
    }

    [Fact]
    public void JeffreysLowerBound_IsMonotonicWithMoreTruePositives()
    {
        Assert.True(FreeSpinDetectionModel.BetaLowerBound(10, 0) > FreeSpinDetectionModel.BetaLowerBound(2, 0));
        Assert.True(FreeSpinDetectionModel.BetaLowerBound(10, 0) > FreeSpinDetectionModel.BetaLowerBound(10, 1));
    }

    [Fact]
    public void ConservativeMonotonicBounds_NeverLetsStrictCutoffBorrowLooserEvidence()
    {
        var corrected = FreeSpinDetectionModel.ConservativeMonotonicBounds([.90, .20, .30]);
        Assert.Equal(.20, corrected[0], 6);
        Assert.Equal(.20, corrected[1], 6);
        Assert.Equal(.30, corrected[2], 6);
        Assert.All(corrected.Zip(new[] { .90, .20, .30 }), pair => Assert.True(pair.First <= pair.Second));
    }

    [Fact]
    public void Model_FailsOpenForButtonsAndUnavailableData()
    {
        var model = FreeSpinDetectionModel.Train(Array.Empty<FreeSpinCalibrationSample>());
        var decision = model.Evaluate(new FreeSpinWheelContext { HasButtonsDown = true, Values = [1, 1, 1, 1] }, FreeSpinDetectionMode.Suppress, 50);
        Assert.False(decision.ShouldSuppress);
        Assert.Equal(FreeSpinVerdict.Abstain, decision.Verdict);
    }

    [Fact]
    public void EligibleContext_IsObservedInReadOnlyAndSuppressedOnlyInSuppressMode()
    {
        var model = FreeSpinDetectionModel.Train(EligibleSyntheticSamples());
        var context = new FreeSpinWheelContext { Values = [0, 0, 0, 10, 0, 0, 0, 2000], IsHorizontal = false };
        Assert.True(model.IsReady);
        Assert.Contains(model.Cutoffs, c => c.LegitimateFalsePositiveGroups == 0 && c.ConservativeConfidence >= .50);
        Assert.Contains(model.Cutoffs, c => c.ScoreCutoff > .95 && c.LegitimateFalsePositiveGroups == 0 && c.ConservativeConfidence >= .50);
        Assert.Equal(model.Cutoffs.Select(c => c.ScoreCutoff).Distinct().Count(), model.Cutoffs.Count);

        var readOnly = model.Evaluate(context, FreeSpinDetectionMode.ReadOnly, 50);
        var suppress = model.Evaluate(context, FreeSpinDetectionMode.Suppress, 50);

        Assert.Equal(FreeSpinInertiaPhase.Lift, readOnly.LikelyPhase);
        Assert.Equal(FreeSpinVerdict.InertiaCandidate, readOnly.Verdict);
        Assert.False(readOnly.ShouldSuppress);
        Assert.True(suppress.ShouldSuppress);
    }

    [Fact]
    public void Model_RequiresFiveIndependentLegitimateGroups()
    {
        var positivesOnly = FreeSpinDetectionModel.Train(new[] { Sample("lift", "p1", 10), Sample("lift", "p2", 10) });
        Assert.False(positivesOnly.IsReady);
        var tooFewLegitimate = FreeSpinDetectionModel.Train(new[]
        {
            Sample("lift", "p1", 10), Sample("lift", "p2", 10),
            Sample("legitimate-scroll", "n1", 100), Sample("legitimate-scroll", "n2", 100),
            Sample("legitimate-scroll", "n3", 100), Sample("legitimate-scroll", "n4", 100),
        });
        Assert.False(tooFewLegitimate.IsReady);
        Assert.Equal(4, tooFewLegitimate.LegitimateCalibrationGroupCount);
    }

    [Fact]
    public void Model_ReportsAPhaseOnlyWithTwoIndependentGroupsAndUsesGlobalOodScale()
    {
        var samples = new List<FreeSpinCalibrationSample>
        {
            Sample("lift", "lift-1", 10), Sample("lift", "lift-2", 10),
            Sample("landing", "landing-1", 30),
        };
        for (var i = 0; i < 5; i++) samples.Add(Sample("legitimate-scroll", "n" + i, 100));
        var model = FreeSpinDetectionModel.Train(samples);
        Assert.True(model.IsReady);
        Assert.True(model.InDistributionDistance >= 0);
        var liveContext = new FreeSpinWheelContext { Values = [0, 0, 0, 10, 0, 0, 0, 2000], IsHorizontal = false };
        Assert.True(model.IsInDistribution(liveContext));
        var decision = model.Evaluate(liveContext, FreeSpinDetectionMode.ReadOnly, 50);
        Assert.Equal(FreeSpinInertiaPhase.Lift, decision.LikelyPhase);
        Assert.False(decision.ShouldSuppress);
    }

    [Fact]
    public void CausalHistory_ResetRemovesPriorWheelContext()
    {
        var history = new FreeSpinCausalHistory();
        history.CommitWheel(100, 120);
        history.Reset();
        Assert.Equal(0, history.CreateForWheel(200, 120, false)!.Values[0]);
    }

    [Fact]
    public void DetectionPolicyTransition_ResetsTheServiceCausalHistory()
    {
        using var hook = new MouseHookService();
        using var service = new FreeSpinDetectionService(hook);
        service.Configure(true, FreeSpinDetectionMode.ReadOnly, 90);
        typeof(FreeSpinDetectionService).GetMethod("OnWheel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service,
            [null, new MouseWheelHookEventArgs(120, false, false, new NativeMethods.POINT())]);
        service.Configure(true, FreeSpinDetectionMode.Suppress, 90);
        var history = (FreeSpinCausalHistory)typeof(FreeSpinDetectionService).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        Assert.Equal(0, history.CreateForWheel(System.Diagnostics.Stopwatch.GetTimestamp(), 120, false)!.Values[0]);
    }

    [Fact]
    public void DetectionSettings_DefaultCloneAndClampAreConservative()
    {
        var settings = new AppSettings { FreeSpinSuppressionConfidenceThreshold = 2, FreeSpinDetectionMode = FreeSpinDetectionMode.Suppress };
        Assert.Equal(50, settings.FreeSpinSuppressionConfidenceThreshold);
        var clone = settings.Clone();
        Assert.Equal(FreeSpinDetectionMode.Suppress, clone.FreeSpinDetectionMode);
        Assert.Equal(50, clone.FreeSpinSuppressionConfidenceThreshold);
        clone.FreeSpinSuppressionConfidenceThreshold = 200;
        Assert.Equal(99, clone.FreeSpinSuppressionConfidenceThreshold);
    }

    [Fact]
    public void PreHandledWheel_NeverQueuesMotionInTheSmoothingCoordinator()
    {
        using var hook = new MouseHookService();
        using var coordinator = new ScrollCoordinator(new ProfileManager(DefaultSettings.CreateAppSettings()), hook, new ScrollInjector(), new ActiveAppResolver());
        var args = new MouseWheelHookEventArgs(120, false, false, new NativeMethods.POINT()) { Handled = true };
        typeof(ScrollCoordinator).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(coordinator, [null, args]);
        var vertical = typeof(ScrollCoordinator).GetField("_vertical", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
        var quiet = (bool)vertical.GetType().GetMethod("IsQuiet")!.Invoke(vertical, null)!;
        Assert.True(quiet);
    }

    private static FreeSpinCalibrationSample Sample(string phase, string id, int delta) => new()
    {
        Id = id, Phase = phase, StartedStopwatchTicks = 1, CompletedStopwatchTicks = 1 + System.Diagnostics.Stopwatch.Frequency,
        Events =
        [
            new FreeSpinCalibrationEvent { Kind = "wheel", StopwatchTicks = 10, WheelDelta = delta },
            new FreeSpinCalibrationEvent { Kind = "wheel", StopwatchTicks = 10 + System.Diagnostics.Stopwatch.Frequency / 10, WheelDelta = delta },
        ],
    };

    private static IEnumerable<FreeSpinCalibrationSample> EligibleSyntheticSamples()
    {
        // Ten positives leave nine phase-matched groups in every leave-one-group-out fold,
        // so the exact empirical score of 1.0 is also reachable by live 9-NN inference.
        for (var i = 0; i < 10; i++) yield return Sample("lift", "lift-" + i, 10);
        for (var i = 0; i < 6; i++) yield return Sample("legitimate-scroll", "legitimate-" + i, 100);
    }
}
