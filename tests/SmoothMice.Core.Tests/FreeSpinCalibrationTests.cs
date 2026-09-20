using SmoothMice.Core.Diagnostics;
using Xunit;

namespace SmoothMice.Core.Tests;

public class FreeSpinCalibrationTests
{
    [Fact]
    public void Completion_contract_rejects_short_sessions_and_accepts_silent_lift_after_half_second()
    {
        var started = DateTimeOffset.UtcNow;
        var lift = new FreeSpinCalibrationSession(FreeSpinCalibrationPhase.Lift, 40, started, 1_000);

        Assert.False(lift.TryValidateCompletion(1_000, out var immediateMessage));
        Assert.Equal("Wait at least 0.5 s before finishing.", immediateMessage);
        Assert.True(lift.TryValidateCompletion(1_000 + FreeSpinCalibrationSession.MinimumDurationStopwatchTicks, out var acceptedMessage));
        Assert.Null(acceptedMessage);
    }

    [Fact]
    public void Legitimate_scroll_requires_a_nonzero_wheel_after_minimum_duration()
    {
        var started = DateTimeOffset.UtcNow;
        var session = new FreeSpinCalibrationSession(FreeSpinCalibrationPhase.LegitimateScroll, 40, started, 10);
        var completed = 10 + FreeSpinCalibrationSession.MinimumDurationStopwatchTicks;

        Assert.False(session.TryValidateCompletion(completed, out var missingWheelMessage));
        Assert.Equal("Perform at least one scroll before finishing the legitimate-scroll sample.", missingWheelMessage);
        session.Record(new FreeSpinRawInputEvent { Kind = FreeSpinRawEventKind.Wheel, TimestampUtc = started, StopwatchTicks = 11, WheelDelta = 120, WheelAxis = "vertical" });
        Assert.True(session.TryValidateCompletion(completed, out var acceptedMessage));
        Assert.Null(acceptedMessage);
    }

    [Fact]
    public void Session_preserves_phase_timing_deltas_and_button_mask()
    {
        var started = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var session = new FreeSpinCalibrationSession(FreeSpinCalibrationPhase.Reposition, 40, started, 100);
        session.Record(new FreeSpinRawInputEvent { Kind = FreeSpinRawEventKind.Move, TimestampUtc = started, StopwatchTicks = 101, X = 10, Y = 15 });
        session.Record(new FreeSpinRawInputEvent { Kind = FreeSpinRawEventKind.Button, TimestampUtc = started.AddMilliseconds(1), StopwatchTicks = 102, X = 12, Y = 19, Button = FreeSpinMouseButton.Left, IsButtonDown = true });
        session.Record(new FreeSpinRawInputEvent { Kind = FreeSpinRawEventKind.Wheel, TimestampUtc = started.AddMilliseconds(2), StopwatchTicks = 103, X = 14, Y = 20, WheelDelta = 120, WheelAxis = "vertical" });
        var sample = session.Complete(started.AddMilliseconds(3), 104);

        Assert.Equal("reposition", sample.Phase);
        Assert.Equal(40, sample.TargetSnapshot);
        Assert.Equal(3, sample.Events.Count);
        Assert.Equal(2, sample.Events[1].Dx);
        Assert.Equal(4, sample.Events[1].Dy);
        Assert.Equal(1, sample.Events[1].ButtonMask);
        Assert.Equal("leftDown", sample.Events[1].ButtonTransition);
        Assert.Equal(120, sample.Events[2].WheelDelta);
    }

    [Fact]
    public void Session_is_bounded_and_phase_labels_are_distinct()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new FreeSpinCalibrationSession(FreeSpinCalibrationPhase.Lift, 2, now, 1, maximumEvents: 2);
        for (var i = 0; i < 3; i++) session.Record(new FreeSpinRawInputEvent { Kind = FreeSpinRawEventKind.Move, TimestampUtc = now, StopwatchTicks = i, X = i, Y = i });
        var sample = session.Complete(now, 4);
        Assert.Equal(2, sample.Events.Count);
        Assert.Equal(1, sample.DroppedEventCount);
        Assert.Equal("lift", sample.Phase);
        Assert.NotEqual(FreeSpinCalibrationSession.ToStorageLabel(FreeSpinCalibrationPhase.Landing), sample.Phase);
        Assert.Equal(30, FreeSpinCalibrationSession.ClampTarget(2));
        Assert.Equal(50, FreeSpinCalibrationSession.ClampTarget(99));
    }
}
