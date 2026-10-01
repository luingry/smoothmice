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
    public void DetectionPolicyTransition_ResetsTheWobbleState()
    {
        using var hook = new MouseHookService();
        using var service = new FreeSpinDetectionService(hook);
        service.Configure(true, FreeSpinDetectionMode.ReadOnly, false);
        typeof(FreeSpinDetectionService).GetMethod("OnWheel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service,
            [null, new MouseWheelHookEventArgs(120, false, false, new NativeMethods.POINT())]);
        service.Configure(true, FreeSpinDetectionMode.Suppress, false);
        var wobble = (FreeSpinWobblePolicy)typeof(FreeSpinDetectionService).GetField("_wobble", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        // A reversed pulse right after the transition must not be treated as a rebound of the old pulse.
        Assert.NotEqual(FreeSpinWobbleAction.Suppress, wobble.OnWheel(System.Diagnostics.Stopwatch.GetTimestamp(), -120, false).Action);
    }

    [Fact]
    public void DetectionSettings_DefaultsAreConservativeAndCloned()
    {
        var defaults = DefaultSettings.CreateAppSettings();
        Assert.False(defaults.FreeSpinInertiaSuppressionEnabled);
        Assert.Equal(FreeSpinDetectionMode.ReadOnly, defaults.FreeSpinDetectionMode);
        Assert.False(defaults.FreeSpinDecisionLogEnabled);

        var settings = new AppSettings { FreeSpinInertiaSuppressionEnabled = true, FreeSpinDetectionMode = FreeSpinDetectionMode.Suppress, FreeSpinDecisionLogEnabled = true };
        var clone = settings.Clone();
        Assert.True(clone.FreeSpinInertiaSuppressionEnabled);
        Assert.Equal(FreeSpinDetectionMode.Suppress, clone.FreeSpinDetectionMode);
        Assert.True(clone.FreeSpinDecisionLogEnabled);
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
}
