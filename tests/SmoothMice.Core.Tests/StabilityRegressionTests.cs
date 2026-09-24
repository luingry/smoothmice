using System.Reflection;
using SmoothMice.App.ViewModels;
using SmoothMice.Core.Config;
using SmoothMice.Core.Profiles;
using SmoothMice.Core.Scrolling;
using SmoothMice.Infrastructure.Windows;
using Xunit;

namespace SmoothMice.Core.Tests;

public class StabilityRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tray_toggle_survives_persistence_and_keeps_selected_profile_edits(bool appSelected)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var manager = new ProfileManager(DefaultSettings.CreateAppSettings());
                if (appSelected) manager.TryAddAppProfile("example.exe", "Example");
                MainViewModel? vm = null;
                var saves = 0;
                vm = new MainViewModel(manager, () => { vm!.Save(); saves++; }, () => { });
                vm.StepSizePx = 73;
                var selectedId = vm.SelectedProfile!.Id;
                var initialEnabled = manager.Snapshot.Profiles.First(p => p.IsGlobal).Settings.Enabled;

                vm.ToggleGlobalEnabled();
                vm.Save(); // Closing the settings window must not undo the command either.
                Assert.Equal(!initialEnabled, manager.Snapshot.Profiles.First(p => p.IsGlobal).Settings.Enabled);
                Assert.Equal(73, manager.Snapshot.Profiles.First(p => p.Id == selectedId).Settings.StepSizePx);
                Assert.Equal(selectedId, vm.SelectedProfile.Id);
                Assert.Equal(appSelected ? initialEnabled : !initialEnabled, vm.ProfileEnabled);

                vm.ToggleGlobalEnabled();
                Assert.Equal(initialEnabled, manager.Snapshot.Profiles.First(p => p.IsGlobal).Settings.Enabled);
                Assert.Equal(2, saves);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Target_transition_discards_both_axes_and_acceleration_only_when_destination_changes(bool changeTarget)
    {
        using var hook = new MouseHookService();
        using var coordinator = new ScrollCoordinator(new ProfileManager(DefaultSettings.CreateAppSettings()),
            hook, new ScrollInjector(), new ActiveAppResolver());
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(ScrollCoordinator);
        var vertical = (SmoothScrollEngine)type.GetField("_vertical", flags)!.GetValue(coordinator)!;
        var horizontal = (SmoothScrollEngine)type.GetField("_horizontal", flags)!.GetValue(coordinator)!;
        var settings = DefaultSettings.CreateGlobalProfileSettings();
        vertical.PushPhysicalDelta(120, settings, 1, 0);
        horizontal.PushPhysicalDelta(120, settings, 1, 0);
        type.GetField("_cachedHwnd", flags)!.SetValue(coordinator, new IntPtr(100));
        type.GetField("_smoothedIntervalMs", flags)!.SetValue(coordinator, 10.0);
        type.GetField("_lastEventMs", flags)!.SetValue(coordinator, 20L);
        type.GetMethod("PrepareTarget", flags)!.Invoke(coordinator, [new IntPtr(changeTarget ? 200 : 100)]);

        Assert.Equal(changeTarget, vertical.IsQuiet());
        Assert.Equal(changeTarget, horizontal.IsQuiet());
        Assert.Equal(changeTarget ? 1L : 0L, type.GetField("_sessionGeneration", flags)!.GetValue(coordinator));
        Assert.Equal(changeTarget ? -1L : 20L, type.GetField("_lastEventMs", flags)!.GetValue(coordinator));
        Assert.Equal(changeTarget ? 400.0 : 10.0, type.GetField("_smoothedIntervalMs", flags)!.GetValue(coordinator));
        if (changeTarget)
        {
            Assert.Equal(0, vertical.Tick(1000));
            Assert.Equal(0, horizontal.Tick(1000));
            vertical.PushPhysicalDelta(120, settings, 1, 1000);
            Assert.Equal((int)(120 * ScrollMath.StepScale(settings.StepSizePx)), vertical.Tick(2000));
        }
    }
}
