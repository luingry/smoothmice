using SmoothMice.Core.Config;
using SmoothMice.Core.Updates;

namespace SmoothMice.Core.Profiles;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool AutoStartOnLogin { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string SelectedProfileId { get; set; } = DefaultSettings.GlobalProfileId;
    public List<ScrollProfile> Profiles { get; set; } = [];

    public UpdateCheckFrequency UpdateCheckFrequency { get; set; } = UpdateCheckFrequency.DailyOnStartup;

    /// <summary>UTC instant of the last successful online update check (used for weekly/monthly spacing).</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        AutoStartOnLogin = AutoStartOnLogin,
        Enabled = Enabled,
        SelectedProfileId = SelectedProfileId,
        Profiles = Profiles.Select(p => p.Clone()).ToList(),
        UpdateCheckFrequency = UpdateCheckFrequency,
        LastUpdateCheckUtc = LastUpdateCheckUtc,
    };
}
