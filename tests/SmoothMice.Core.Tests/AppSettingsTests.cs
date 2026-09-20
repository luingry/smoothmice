using SmoothMice.Core.Config;
using SmoothMice.Core.Profiles;
using SmoothMice.App.ViewModels;
using SmoothMice.Infrastructure.Persistence;
using Xunit;

namespace SmoothMice.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Adding_or_reusing_an_executable_selects_its_profile_without_duplicates()
    {
        var manager = new ProfileManager(DefaultSettings.CreateAppSettings());
        var persistCount = 0;
        var viewModel = new MainViewModel(manager, () => persistCount++, () => { });

        viewModel.AddOrSelectAppProfile("Example.exe");
        var added = Assert.Single(manager.Snapshot.Profiles.Where(profile =>
            string.Equals(profile.ExecutableName, "Example.exe", StringComparison.OrdinalIgnoreCase)));

        viewModel.SwitchProfile(DefaultSettings.GlobalProfileName);
        viewModel.AddOrSelectAppProfile("example.EXE");

        var snapshot = manager.Snapshot;
        Assert.Single(snapshot.Profiles.Where(profile =>
            string.Equals(profile.ExecutableName, "Example.exe", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(added.Id, snapshot.SelectedProfileId);
        Assert.Equal(added.Id, viewModel.SelectedProfile?.Id);
        Assert.True(persistCount >= 2);
    }

    [Fact]
    public void Default_settings_leave_free_spin_inertia_suppression_disabled()
    {
        var settings = DefaultSettings.CreateAppSettings();

        Assert.False(settings.FreeSpinInertiaSuppressionEnabled);
    }

    [Fact]
    public void Clone_preserves_free_spin_inertia_suppression_setting()
    {
        var settings = new AppSettings { FreeSpinInertiaSuppressionEnabled = true };

        var clone = settings.Clone();

        Assert.True(clone.FreeSpinInertiaSuppressionEnabled);
        settings.FreeSpinInertiaSuppressionEnabled = false;
        Assert.True(clone.FreeSpinInertiaSuppressionEnabled);
    }

    [Fact]
    public void Profile_manager_snapshot_retains_free_spin_inertia_suppression_setting()
    {
        var manager = new ProfileManager(DefaultSettings.CreateAppSettings());

        manager.SetFreeSpinInertiaSuppressionEnabled(true);

        Assert.True(manager.Snapshot.FreeSpinInertiaSuppressionEnabled);
    }

    [Fact]
    public void Default_settings_leave_game_bypass_disabled()
    {
        var settings = DefaultSettings.CreateAppSettings();

        Assert.False(settings.DoNotActivateInGames);
    }

    [Fact]
    public void Default_settings_leave_dark_mode_disabled()
    {
        var settings = DefaultSettings.CreateAppSettings();

        Assert.False(settings.DarkMode);
    }

    [Fact]
    public void Clone_preserves_dark_mode_setting()
    {
        var settings = new AppSettings { DarkMode = true };

        var clone = settings.Clone();

        Assert.True(clone.DarkMode);
        settings.DarkMode = false;
        Assert.True(clone.DarkMode);
    }

    [Fact]
    public void Profile_manager_snapshot_retains_dark_mode_update()
    {
        var manager = new ProfileManager(DefaultSettings.CreateAppSettings());

        manager.SetDarkMode(true);

        Assert.True(manager.DarkMode);
        Assert.True(manager.Snapshot.DarkMode);
    }

    [Fact]
    public void Clone_preserves_game_bypass_setting()
    {
        var settings = new AppSettings { DoNotActivateInGames = true };

        var clone = settings.Clone();

        Assert.True(clone.DoNotActivateInGames);
        settings.DoNotActivateInGames = false;
        Assert.True(clone.DoNotActivateInGames);
    }

    [Fact]
    public void Profile_manager_snapshot_retains_game_bypass_update()
    {
        var manager = new ProfileManager(DefaultSettings.CreateAppSettings());

        manager.SetDoNotActivateInGames(true);

        Assert.True(manager.Snapshot.DoNotActivateInGames);
    }

    [Fact]
    public void Json_repository_reload_retains_game_bypass_setting()
    {
        var path = Path.Combine(Path.GetTempPath(), "SmoothMice.Tests", Guid.NewGuid() + ".json");
        var repository = new JsonSettingsRepository(path);

        try
        {
            repository.Save(new AppSettings { DoNotActivateInGames = true });

            Assert.True(repository.LoadOrCreate().DoNotActivateInGames);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }

    [Fact]
    public void Json_repository_defaults_to_light_when_dark_mode_field_is_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), "SmoothMice.Tests", Guid.NewGuid() + ".json");
        var repository = new JsonSettingsRepository(path);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"schemaVersion\":1}");

            Assert.False(repository.LoadOrCreate().DarkMode);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }

    [Fact]
    public void View_model_dark_mode_toggle_persists_and_applies_theme_immediately()
    {
        var path = Path.Combine(Path.GetTempPath(), "SmoothMice.Tests", Guid.NewGuid() + ".json");
        var repository = new JsonSettingsRepository(path);
        var manager = new ProfileManager(DefaultSettings.CreateAppSettings());
        var applyCount = 0;
        var appliedDarkMode = false;
        var viewModel = new MainViewModel(
            manager,
            () => repository.Save(manager.Snapshot),
            () => { },
            darkMode =>
            {
                applyCount++;
                appliedDarkMode = darkMode;
            });

        try
        {
            applyCount = 0;
            viewModel.DarkMode = true;

            Assert.True(manager.Snapshot.DarkMode);
            Assert.True(repository.LoadOrCreate().DarkMode);
            Assert.True(appliedDarkMode);
            Assert.Equal(1, applyCount);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }

    [Fact]
    public void View_model_game_bypass_toggle_persists_through_manager_snapshot_immediately()
    {
        var path = Path.Combine(Path.GetTempPath(), "SmoothMice.Tests", Guid.NewGuid() + ".json");
        var repository = new JsonSettingsRepository(path);
        var manager = new ProfileManager(DefaultSettings.CreateAppSettings());
        var viewModel = new MainViewModel(manager, () => repository.Save(manager.Snapshot), () => { });

        try
        {
            viewModel.DoNotActivateInGames = true;

            Assert.True(repository.LoadOrCreate().DoNotActivateInGames);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(40, 40)]
    [InlineData(50, 50)]
    [InlineData(4, 30)]
    [InlineData(99, 50)]
    public void Calibration_targets_are_clamped_and_cloned(int entered, int expected)
    {
        var settings = new AppSettings { FreeSpinLiftTarget = entered };
        var clone = settings.Clone();
        Assert.Equal(expected, settings.FreeSpinLiftTarget);
        Assert.Equal(expected, clone.FreeSpinLiftTarget);
    }

    [Fact]
    public void Default_and_profile_targets_are_independent()
    {
        var defaults = DefaultSettings.CreateAppSettings();
        Assert.Equal(40, defaults.FreeSpinLiftTarget);
        Assert.Equal(40, defaults.FreeSpinLandingTarget);
        Assert.Equal(40, defaults.FreeSpinRepositionTarget);
        Assert.Equal(40, defaults.FreeSpinLegitimateScrollTarget);

        var manager = new ProfileManager(defaults);
        manager.SetFreeSpinCalibrationTarget(SmoothMice.Core.Diagnostics.FreeSpinCalibrationPhase.Landing, 50);
        manager.SetFreeSpinCalibrationTarget(SmoothMice.Core.Diagnostics.FreeSpinCalibrationPhase.LegitimateScroll, 30);
        var saved = manager.Snapshot;
        Assert.Equal(40, saved.FreeSpinLiftTarget);
        Assert.Equal(50, saved.FreeSpinLandingTarget);
        Assert.Equal(40, saved.FreeSpinRepositionTarget);
        Assert.Equal(30, saved.FreeSpinLegitimateScrollTarget);
    }
}
