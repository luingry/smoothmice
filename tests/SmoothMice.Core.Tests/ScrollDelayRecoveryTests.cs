using SmoothMice.Core.Config;
using SmoothMice.Core.Scrolling;
using Xunit;

namespace SmoothMice.Core.Tests;

public class ScrollDelayRecoveryTests
{
    [Theory]
    [InlineData(48, false)]
    [InlineData(100, false)]
    [InlineData(100, true)]
    [InlineData(5000, true)]
    public void Stall_does_not_dump_the_backlog_and_preserves_distance(int stall, bool smallStep)
    {
        var settings = DefaultSettings.CreateGlobalProfileSettings();
        if (smallStep)
        {
            settings.StepSizePx = 5;
            settings.AnimationTimeMs = 300;
            settings.AttackTimeMs = 500;
        }
        var baseline = new SmoothScrollEngine();
        baseline.PushPhysicalDelta(120, settings, 1, 0);
        int normalPeak = 0, expected = 0;
        for (long t = 4; !baseline.IsQuiet(); t += 4)
        {
            var delta = baseline.Tick(t);
            normalPeak = Math.Max(normalPeak, delta);
            expected += delta;
        }
        var engine = new SmoothScrollEngine();
        engine.PushPhysicalDelta(120, settings, 1, 0);
        int total = 0;
        for (long t = 4; t <= 20; t += 4) total += engine.Tick(t);
        var resumedAt = 24L + stall;
        var resumed = engine.Tick(resumedAt);
        Assert.InRange(resumed, 0, normalPeak * 2 + 1);
        total += resumed;
        for (long t = resumedAt + 4; !engine.IsQuiet() && t < resumedAt + 3000; t += 4)
        {
            var delta = engine.Tick(t);
            Assert.InRange(delta, 0, normalPeak * 2 + 1);
            total += delta;
        }
        Assert.True(engine.IsQuiet());
        Assert.InRange(total, expected - 1, expected + 1);
    }
}
