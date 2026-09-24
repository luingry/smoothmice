using System.Reflection;
using System.Runtime.InteropServices;
using SmoothMice.Infrastructure.Windows;
using Xunit;

namespace SmoothMice.Core.Tests;

public class ScrollDeliveryTests
{
    [Fact]
    public async Task Reversal_rejects_a_tick_already_waiting_for_hook_delivery_without_blocking_input()
    {
        var guard = new WheelInjectionGuard();
        using var hook = new MouseHookService { InjectionGuard = guard };
        guard.ObserveInput(new IntPtr(100), 120, false);
        var calculatedStamp = guard.Capture(false);
        using var sendStarted = new ManualResetEventSlim();
        using var arriveAtHook = new ManualResetEventSlim();
        var delivered = Task.Run(() =>
        {
            sendStarted.Set();
            if (!arriveAtHook.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
            return InvokeInjectedWheel(hook, calculatedStamp, horizontal: false);
        });
        Assert.True(sendStarted.Wait(TimeSpan.FromSeconds(3)));
        try
        {
            // Reversal must finish while native delivery is still blocked, not wait on SendInput.
            guard.ObserveInput(new IntPtr(100), -120, false);
            Assert.False(guard.IsCurrent(calculatedStamp, false));
        }
        finally { arriveAtHook.Set(); }
        Assert.Equal(new IntPtr(1), await delivered.WaitAsync(TimeSpan.FromSeconds(3))); // The real hook swallowed the stale tick.
    }

    [Fact]
    public void Epoch_is_axis_specific_and_changes_even_when_the_old_engine_has_finished()
    {
        var guard = new WheelInjectionGuard();
        guard.ObserveInput(new IntPtr(100), 120, false);
        var v = guard.Capture(false);
        guard.ObserveInput(new IntPtr(100), 120, true);
        var h = guard.Capture(true);
        guard.ObserveInput(new IntPtr(100), 120, false);
        Assert.Equal(v, guard.Capture(false));
        guard.ObserveInput(new IntPtr(100), -120, true);
        Assert.False(guard.IsCurrent(h, true));
        Assert.True(guard.IsCurrent(v, false));
        guard.ObserveInput(new IntPtr(200), 120, false);
        Assert.False(guard.IsCurrent(v, false));
        var newStamp = guard.Capture(false);
        guard.Invalidate();
        Assert.True(guard.ShouldSuppress(new IntPtr(newStamp), false));
        Assert.False(guard.ShouldSuppress(IntPtr.Zero, false));
        Assert.False(guard.ShouldSuppress(new IntPtr(0x4e505554), false));
    }

    [Fact]
    public void Current_injection_stays_out_of_smoothing_and_stale_horizontal_is_blocked()
    {
        var guard = new WheelInjectionGuard();
        using var hook = new MouseHookService { InjectionGuard = guard };
        int reprocessed = 0;
        hook.MouseWheel += (_, _) => reprocessed++;
        guard.ObserveInput(new IntPtr(100), 120, true);
        var stamp = guard.Capture(true);
        InvokeInjectedWheel(hook, stamp, horizontal: true);
        Assert.Equal(0, reprocessed);
        guard.ObserveInput(new IntPtr(100), -120, true);
        Assert.Equal(new IntPtr(1), InvokeInjectedWheel(hook, stamp, horizontal: true));
        Assert.Equal(0, reprocessed);
    }

    private static IntPtr InvokeInjectedWheel(MouseHookService hook, int stamp, bool horizontal)
    {
        var info = new NativeMethods.MSLLHOOKSTRUCT
        {
            flags = NativeMethods.LlmhfInjected,
            dwExtraInfo = new IntPtr(stamp),
            mouseData = 120u << 16,
        };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            return (IntPtr)typeof(MouseHookService).GetMethod("HookCallback", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(hook, [0, new IntPtr(horizontal ? NativeMethods.WmMousehwheel : NativeMethods.WmMousewheel), ptr])!;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scheduler_is_idle_when_stopped_resumes_and_disposes_without_waiting_for_callback(bool fallback)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int count = 0, concurrent = 0, maximumConcurrent = 0;
        var scheduler = new ScrollTickScheduler(() =>
        {
            var active = Interlocked.Increment(ref concurrent);
            maximumConcurrent = Math.Max(maximumConcurrent, active);
            Interlocked.Increment(ref count);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(3));
            Interlocked.Decrement(ref concurrent);
        }, forceFallback: fallback);
        try
        {
            await Task.Delay(40);
            Assert.Equal(0, Volatile.Read(ref count));
            scheduler.Start();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            scheduler.Stop(); // Must not block on the callback.
            release.Set();
            await Task.Delay(60);
            Assert.Equal(1, Volatile.Read(ref count));
            entered.Reset();
            scheduler.Start();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            release.Set();
            scheduler.Dispose();
            await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        }
        Assert.Equal(1, maximumConcurrent);
        var stoppedAt = Volatile.Read(ref count);
        await Task.Delay(30);
        Assert.Equal(stoppedAt, Volatile.Read(ref count));
    }
}
