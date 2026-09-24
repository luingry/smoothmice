using SmoothMice.Core.Config;
using SmoothMice.Core.Profiles;
using SmoothMice.Infrastructure.Persistence;
using Xunit;

namespace SmoothMice.Core.Tests;

public class JsonSettingsRepositoryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Valid_json_with_null_profile_recovers_without_crashing_startup(bool hasBackup)
    {
        var path = NewTempSettingsPath();
        var repo = new JsonSettingsRepository(path);
        if (hasBackup)
        {
            repo.Save(WithCustomAppProfile());
            repo.Save(WithCustomAppProfile());
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"profiles\":[null]}");

        var manager = new ProfileManager(repo.LoadOrCreate());
        Assert.Contains(manager.Snapshot.Profiles, p => p.IsGlobal);
        Assert.Equal(hasBackup, manager.Snapshot.Profiles.Any(p => p.Id == "custom-1"));
        if (!hasBackup)
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "settings.json.corrupt-*"));
    }

    private static string NewTempSettingsPath() =>
        Path.Combine(Path.GetTempPath(), $"SmoothMiceTests_{Guid.NewGuid():N}", "settings.json");

    private static AppSettings WithCustomAppProfile()
    {
        var settings = DefaultSettings.CreateAppSettings();
        settings.Profiles.Add(new ScrollProfile
        {
            Id = "custom-1",
            DisplayName = "My Custom App",
            ExecutableName = "customapp.exe",
            IsGlobal = false,
            Settings = DefaultSettings.CreateGlobalProfileSettings(),
        });
        settings.SelectedProfileId = "custom-1";
        return settings;
    }

    [Fact]
    public void Save_then_LoadOrCreate_round_trips_custom_profiles()
    {
        var path = NewTempSettingsPath();
        var repo = new JsonSettingsRepository(path);
        var original = WithCustomAppProfile();

        repo.Save(original);
        var loaded = repo.LoadOrCreate();

        Assert.Contains(loaded.Profiles, p => p.Id == "custom-1" && p.ExecutableName == "customapp.exe");
    }

    [Fact]
    public void Save_leaves_a_backup_file_that_LoadOrCreate_recovers_from_when_primary_is_corrupted()
    {
        var path = NewTempSettingsPath();
        var repo = new JsonSettingsRepository(path);

        repo.Save(WithCustomAppProfile());
        // A second save promotes the first good file to the .bak slot.
        repo.Save(WithCustomAppProfile());

        // Simulate a torn write: the process died mid-write, leaving truncated/invalid JSON.
        File.WriteAllText(path, "{ \"schemaVersion\": 1, \"profiles\": [ { \"id\": \"cu");

        var recovered = repo.LoadOrCreate();

        Assert.Contains(recovered.Profiles, p => p.Id == "custom-1");
    }

    [Fact]
    public void LoadOrCreate_quarantines_an_unreadable_primary_file_instead_of_silently_discarding_it()
    {
        var path = NewTempSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not valid json at all");

        var repo = new JsonSettingsRepository(path);
        var loaded = repo.LoadOrCreate();

        // Falls back to defaults (no backup available)...
        Assert.True(loaded.Profiles.Count == 1 && loaded.Profiles[0].IsGlobal);

        // ...but the unreadable original is preserved for forensic recovery, not deleted.
        var quarantined = Directory.GetFiles(Path.GetDirectoryName(path)!, "settings.json.corrupt-*");
        Assert.Single(quarantined);
        Assert.Equal("not valid json at all", File.ReadAllText(quarantined[0]));
    }

    [Fact]
    public void Save_never_leaves_the_primary_file_missing_or_empty_even_under_concurrent_writers()
    {
        var path = NewTempSettingsPath();
        var repo = new JsonSettingsRepository(path);
        repo.Save(WithCustomAppProfile());

        var errors = new List<Exception>();
        Parallel.For(0, 20, i =>
        {
            try
            {
                var s = WithCustomAppProfile();
                s.FreeSpinLiftTarget = 30 + (i % 20);
                repo.Save(s);
            }
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex);
            }
        });

        Assert.Empty(errors);
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);

        // The file left behind must always be valid, parseable JSON — never a torn write from
        // two threads racing on the same temp/destination path.
        var final = repo.LoadOrCreate();
        Assert.Contains(final.Profiles, p => p.Id == "custom-1");
    }
}
