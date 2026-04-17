using System.Text.Json;
using System.Text.Json.Serialization;
using SmoothMice.Core.Config;
using SmoothMice.Core.Profiles;

namespace SmoothMice.Infrastructure.Persistence;

public sealed class JsonSettingsRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string FilePath { get; }

    public JsonSettingsRepository(string? filePath = null)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmoothMice");
        FilePath = filePath ?? Path.Combine(dir, "settings.json");
    }

    public AppSettings LoadOrCreate()
    {
        try
        {
            if (!File.Exists(FilePath))
                return DefaultSettings.CreateAppSettings();

            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
            return loaded ?? DefaultSettings.CreateAppSettings();
        }
        catch
        {
            return DefaultSettings.CreateAppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, Options);
        File.WriteAllText(FilePath, json);
    }
}
