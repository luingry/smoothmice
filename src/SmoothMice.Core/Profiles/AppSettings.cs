using SmoothMice.Core.Config;

namespace SmoothMice.Core.Profiles;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool AutoStartOnLogin { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string SelectedProfileId { get; set; } = DefaultSettings.GlobalProfileId;
    public List<ScrollProfile> Profiles { get; set; } = [];

    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        AutoStartOnLogin = AutoStartOnLogin,
        Enabled = Enabled,
        SelectedProfileId = SelectedProfileId,
        Profiles = Profiles.Select(p => p.Clone()).ToList(),
    };
}
