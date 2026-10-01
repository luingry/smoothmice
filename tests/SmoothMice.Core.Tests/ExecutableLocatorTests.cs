using SmoothMice.Core.Config;
using SmoothMice.Core.Profiles;
using SmoothMice.Infrastructure.Windows;
using Xunit;

namespace SmoothMice.Core.Tests;

public class ExecutableLocatorTests
{
    [Theory]
    [InlineData(@"C:\program files\bambu studio\bambu-studio.exe.FriendlyAppName", @"C:\program files\bambu studio\bambu-studio.exe")]
    [InlineData(@"D:\Games\GoWR.exe.ApplicationCompany", @"D:\Games\GoWR.exe")]
    [InlineData(@"C:\odd.exe.folder\app.exe.FriendlyAppName", @"C:\odd.exe.folder\app.exe")]
    [InlineData(@"C:\Tools\app.exe", @"C:\Tools\app.exe")]
    [InlineData(@"@C:\Windows\System32\shell32.dll,-22019", null)]
    public void MuiCache_value_names_yield_the_executable_path(string valueName, string? expected)
    {
        Assert.Equal(expected, ExecutableLocator.ExecutablePathFromMuiCacheValueName(valueName));
    }

    [Fact]
    public void Recorded_paths_update_matching_app_profiles_only()
    {
        var manager = new ProfileManager(DefaultSettings.CreateAppSettings());
        manager.TryAddAppProfile("bambu-studio.exe", "bambu-studio");

        var changed = manager.RecordExecutablePaths(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Bambu-Studio.exe"] = @"C:\Program Files\Bambu Studio\bambu-studio.exe",
            ["other.exe"] = @"C:\other.exe",
        });

        Assert.True(changed);
        var snapshot = manager.Snapshot;
        Assert.Equal(@"C:\Program Files\Bambu Studio\bambu-studio.exe",
            snapshot.Profiles.Single(p => p.ExecutableName == "bambu-studio.exe").ExecutablePath);
        Assert.Null(snapshot.Profiles.Single(p => p.IsGlobal).ExecutablePath);
        Assert.False(manager.RecordExecutablePaths(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bambu-studio.exe"] = @"C:\Program Files\Bambu Studio\bambu-studio.exe",
        }));
    }

    [Fact]
    public void Saving_an_edited_profile_keeps_a_path_learned_after_the_editor_copied_it()
    {
        var manager = new ProfileManager(DefaultSettings.CreateAppSettings());
        manager.TryAddAppProfile("bambu-studio.exe", "bambu-studio");
        var editorCopy = manager.Snapshot.Profiles.Single(p => p.ExecutableName == "bambu-studio.exe").Clone();

        manager.RecordExecutablePaths(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bambu-studio.exe"] = @"C:\Program Files\Bambu Studio\bambu-studio.exe",
        });
        editorCopy.Settings.StepSizePx = 42;
        manager.UpsertEditedProfile(editorCopy);

        var stored = manager.Snapshot.Profiles.Single(p => p.ExecutableName == "bambu-studio.exe");
        Assert.Equal(42, stored.Settings.StepSizePx);
        Assert.Equal(@"C:\Program Files\Bambu Studio\bambu-studio.exe", stored.ExecutablePath);
    }
}
