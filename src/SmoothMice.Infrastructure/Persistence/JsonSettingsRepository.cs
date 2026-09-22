using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SmoothMice.Core.Config;
using SmoothMice.Core.Profiles;

namespace SmoothMice.Infrastructure.Persistence;

/// <summary>
/// Reads/writes <see cref="AppSettings"/> as JSON. Hardened against the two realistic ways this
/// file can otherwise lose a user's profiles: a torn write (process killed / crashed / power loss
/// mid-write) leaving unparsable JSON, and concurrent writers (the UI's live-apply timer and a
/// background update-check both call <see cref="Save"/> on the same <see cref="JsonSettingsRepository"/>
/// instance from different threads).
/// </summary>
public sealed class JsonSettingsRepository
{
    private static readonly JsonSerializerSettings Options = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
    };

    private readonly object _fileLock = new();

    public string FilePath { get; }
    private string BackupPath => FilePath + ".bak";
    private string TempPath => FilePath + ".tmp";

    public JsonSettingsRepository(string? filePath = null)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmoothMice");
        FilePath = filePath ?? Path.Combine(dir, "settings.json");
    }

    public AppSettings LoadOrCreate()
    {
        lock (_fileLock)
        {
            if (TryLoadFrom(FilePath, out var primary))
                return primary!;

            // Primary is missing/corrupt. Fall back to the last known-good copy Save() keeps
            // before ever giving up and handing back hard defaults.
            if (TryLoadFrom(BackupPath, out var fromBackup))
            {
                TryRestoreFromBackup();
                return fromBackup!;
            }

            // Both copies are unreadable (or this is a genuinely fresh install). If a primary
            // file exists but couldn't be parsed, preserve it under a quarantine name instead of
            // silently discarding it — it may still hold recoverable custom profiles.
            QuarantineUnreadableFile();
            return DefaultSettings.CreateAppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_fileLock)
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(settings, Options);

            // Write to a temp file first so a crash/kill/power-loss mid-write only ever corrupts
            // the temp file, never the real settings.json.
            File.WriteAllText(TempPath, json);

            if (File.Exists(FilePath))
            {
                // Atomic on NTFS: swaps temp -> FilePath and FilePath's previous contents -> .bak
                // in one operation, so there is never a moment with a missing or half-written file.
                File.Replace(TempPath, FilePath, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(TempPath, FilePath);
            }
        }
    }

    private static bool TryLoadFrom(string path, out AppSettings? settings)
    {
        settings = null;
        try
        {
            if (!File.Exists(path))
                return false;

            var json = File.ReadAllText(path);
            var loaded = JsonConvert.DeserializeObject<AppSettings>(json, Options);
            if (loaded is null)
                return false;

            settings = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TryRestoreFromBackup()
    {
        try
        {
            File.Copy(BackupPath, FilePath, overwrite: true);
        }
        catch
        {
            // Best effort — the caller already has the settings from the backup in memory;
            // the next successful Save() will re-establish a valid primary file anyway.
        }
    }

    private void QuarantineUnreadableFile()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;

            var quarantinePath = FilePath + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(FilePath, quarantinePath, overwrite: true);
        }
        catch
        {
            // Never let quarantine failure block startup — worst case we lose the forensic copy.
        }
    }
}
