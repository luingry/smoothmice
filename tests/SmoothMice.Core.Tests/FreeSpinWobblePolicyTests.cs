using System.Diagnostics;
using System.Reflection;
using SmoothMice.Core.Diagnostics;
using SmoothMice.Infrastructure.Windows;
using Xunit;

namespace SmoothMice.Core.Tests;

public class FreeSpinWobblePolicyTests
{
    private static long Ms(double ms) => (long)(ms * Stopwatch.Frequency / 1000d);

    /// <summary>Movement ending at <paramref name="endMs"/> at the given speed (px/s), sampled every 5 ms.</summary>
    private static FreeSpinWobblePolicy MovedUntil(double endMs, double pxPerSecond)
    {
        var policy = new FreeSpinWobblePolicy();
        for (var t = endMs - 100; t <= endMs; t += 5) policy.ObserveMove(Ms(t), (int)(t * pxPerSecond / 1000), 0);
        return policy;
    }

    private static FreeSpinWobblePolicy Lifted(double endMs) => MovedUntil(endMs, 800);

    [Fact]
    public void Lift_wobble_drops_the_first_pulse_the_rebound_and_further_rocking()
    {
        // Measured: +120 while the hand jerks the mouse up, -120 ~140 ms later, sometimes more.
        var policy = Lifted(1_000);
        Assert.Equal(FreeSpinWobbleAction.Suppress, policy.OnWheel(Ms(1_040), 120, false).Action);
        Assert.Equal(FreeSpinWobbleAction.Suppress, policy.OnWheel(Ms(1_180), -120, false).Action);
        Assert.Equal(FreeSpinWobbleAction.Suppress, policy.OnWheel(Ms(1_250), -120, false).Action);
    }

    [Fact]
    public void Scrolling_while_the_cursor_still_moves_is_never_dropped()
    {
        var policy = Lifted(1_000);
        Assert.Equal(FreeSpinWobbleAction.Pass, policy.OnWheel(Ms(1_003), -120, false).Action);
        Assert.Equal(FreeSpinWobbleAction.Pass, policy.OnWheel(Ms(1_150), -120, false).Action);
    }

    [Fact]
    public void Scrolling_after_the_cursor_decelerated_to_a_stop_is_never_dropped()
    {
        var policy = MovedUntil(1_000, 100);
        Assert.Equal(FreeSpinWobbleAction.Pass, policy.OnWheel(Ms(1_060), -120, false).Action);
    }

    [Fact]
    public void A_fast_scroll_after_a_dropped_first_pulse_passes_immediately()
    {
        var policy = Lifted(1_000);
        Assert.Equal(FreeSpinWobbleAction.Suppress, policy.OnWheel(Ms(1_040), -120, false).Action);
        for (var t = 1_060; t < 1_300; t += 20)
            Assert.Equal(FreeSpinWobbleAction.Pass, policy.OnWheel(Ms(t), -120, false).Action);
    }

    [Fact]
    public void One_way_spin_drops_a_single_slow_follow_up()
    {
        var policy = Lifted(1_000);
        Assert.Equal(FreeSpinWobbleAction.Suppress, policy.OnWheel(Ms(1_010), 120, false).Action);
        Assert.Equal(FreeSpinWobbleAction.Suppress, policy.OnWheel(Ms(1_150), 120, false).Action);
        // A slow notch-by-notch scroll looks the same, so only one follow-up is dropped.
        Assert.Equal(FreeSpinWobbleAction.Pass, policy.OnWheel(Ms(1_290), 120, false).Action);
        // A reversal long after the gesture is not a rebound.
        Assert.Equal(FreeSpinWobbleAction.Pass, policy.OnWheel(Ms(3_000), -120, false).Action);
    }

    [Fact]
    public void Rebound_after_a_start_that_passed_is_still_inertia()
    {
        var policy = new FreeSpinWobblePolicy();
        Assert.Equal(FreeSpinWobbleAction.Pass, policy.OnWheel(Ms(5_000), 120, false).Action);
        Assert.Equal(FreeSpinWobbleAction.Suppress, policy.OnWheel(Ms(5_140), -120, false).Action);
    }

    [Fact]
    public void Still_mouse_scroll_is_never_dropped()
    {
        var policy = Lifted(1_000);
        for (var t = 3_000; t < 3_400; t += 15)
            Assert.Equal(FreeSpinWobbleAction.Pass, policy.OnWheel(Ms(t), 120, false).Action);
    }

    [Fact]
    public void Held_button_and_horizontal_wheel_disable_the_filter()
    {
        var policy = Lifted(1_000);
        policy.ObserveButton(1, true);
        Assert.Equal(FreeSpinWobbleAction.Pass, policy.OnWheel(Ms(1_040), 120, false).Action);
        Assert.Equal(FreeSpinWobbleAction.Pass, policy.OnWheel(Ms(1_180), -120, false).Action);
        policy.ObserveButton(1, false);
        Assert.Equal(FreeSpinWobbleAction.Pass, Lifted(1_000).OnWheel(Ms(1_040), 120, true).Action);
    }

    [Fact]
    public void Movement_window_forgets_old_motion()
    {
        var policy = Lifted(1_000);
        Assert.True(policy.Movement(Ms(1_000)).PathPx > 0);
        Assert.Equal(0, policy.Movement(Ms(2_000)).PathPx);
    }

    [Fact]
    public void Decision_log_is_off_by_default_and_follows_the_switch_only_while_the_module_is_on()
    {
        var defaults = SmoothMice.Core.Config.DefaultSettings.CreateAppSettings();
        Assert.False(defaults.FreeSpinDecisionLogEnabled);
        var manager = new SmoothMice.Core.Profiles.ProfileManager(defaults);
        manager.SetFreeSpinDecisionLogEnabled(true);
        Assert.True(manager.Snapshot.FreeSpinDecisionLogEnabled);
        Assert.True(manager.Snapshot.Clone().FreeSpinDecisionLogEnabled);

        var directory = Path.Combine(Path.GetTempPath(), "SmoothMice.DecisionLogTests", Guid.NewGuid().ToString("N"));
        try
        {
            using (var hook = new MouseHookService())
            using (var service = new FreeSpinDetectionService(hook, directory))
            {
                service.Configure(true, FreeSpinDetectionMode.Suppress, logDecisions: false);
                Assert.Null(service.DecisionLogPath);

                service.Configure(true, FreeSpinDetectionMode.Suppress, logDecisions: true);
                Assert.StartsWith(directory, service.DecisionLogPath);

                // Turning the module off stops recording even when the switch stays on.
                service.Configure(false, FreeSpinDetectionMode.Suppress, logDecisions: true);
                Assert.Null(service.DecisionLogPath);

                service.Configure(true, FreeSpinDetectionMode.Suppress, logDecisions: true);
                Assert.NotNull(service.DecisionLogPath);
                service.Configure(true, FreeSpinDetectionMode.Suppress, logDecisions: false);
                Assert.Null(service.DecisionLogPath);
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Service_swallows_only_in_suppress_mode_and_never_delays()
    {
        using var hook = new MouseHookService();
        using var service = new FreeSpinDetectionService(hook);
        var onPhysical = typeof(FreeSpinDetectionService).GetMethod("OnPhysicalInput", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var onWheel = typeof(FreeSpinDetectionService).GetMethod("OnWheel", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void SharpMoveEndingNow()
        {
            var now = Stopwatch.GetTimestamp();
            for (var i = 20; i >= 0; i--)
                onPhysical.Invoke(service, [null, new FreeSpinPhysicalInputEventArgs(new FreeSpinRawInputEvent { Kind = FreeSpinRawEventKind.Move, StopwatchTicks = now - Ms(15 + i * 5), X = 1_000 - i * 5, Y = 0 })]);
        }

        service.Configure(true, FreeSpinDetectionMode.Suppress, false);
        SharpMoveEndingNow();
        var lift = new MouseWheelHookEventArgs(120, false, false, new NativeMethods.POINT());
        onWheel.Invoke(service, [null, lift]);
        Assert.True(lift.Handled);

        service.Configure(true, FreeSpinDetectionMode.ReadOnly, false);
        SharpMoveEndingNow();
        var readOnly = new MouseWheelHookEventArgs(120, false, false, new NativeMethods.POINT());
        onWheel.Invoke(service, [null, readOnly]);
        Assert.False(readOnly.Handled);
    }
}
