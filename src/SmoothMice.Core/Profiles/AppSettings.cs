using SmoothMice.Core.Config;
using SmoothMice.Core.Diagnostics;
using SmoothMice.Core.Updates;

namespace SmoothMice.Core.Profiles;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool AutoStartOnLogin { get; set; } = true;
    public string SelectedProfileId { get; set; } = DefaultSettings.GlobalProfileId;
    public List<ScrollProfile> Profiles { get; set; } = [];

    public UpdateCheckFrequency UpdateCheckFrequency { get; set; } = UpdateCheckFrequency.DailyOnStartup;

    /// <summary>Enables Free-Spin Inertia Suppression. Off by default: only free-spin wheels need it.</summary>
    public bool FreeSpinInertiaSuppressionEnabled { get; set; }

    /// <summary>Read-only is deliberately the compatibility default for existing installs.</summary>
    public FreeSpinDetectionMode FreeSpinDetectionMode { get; set; } = FreeSpinDetectionMode.ReadOnly;

    /// <summary>
    /// Writes every Free-Spin decision to Diagnostics\free-spin-live-*.ndjson while the module is on.
    /// Off by default: it is a tuning aid, not something to leave running.
    /// </summary>
    public bool FreeSpinDecisionLogEnabled { get; set; }

    /// <summary>
    /// When enabled, physical wheel input is left untouched for conservatively identified game windows.
    /// This is a global shell preference; it is intentionally not an application profile setting.
    /// </summary>
    public bool DoNotActivateInGames { get; set; }

    /// <summary>Uses the application's neutral dark palette instead of the default light palette.</summary>
    public bool DarkMode { get; set; }

    /// <summary>UTC instant of the last successful online update check (used for weekly/monthly spacing).</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        AutoStartOnLogin = AutoStartOnLogin,
        SelectedProfileId = SelectedProfileId,
        Profiles = Profiles.Select(p => p.Clone()).ToList(),
        UpdateCheckFrequency = UpdateCheckFrequency,
        FreeSpinInertiaSuppressionEnabled = FreeSpinInertiaSuppressionEnabled,
        FreeSpinDetectionMode = FreeSpinDetectionMode,
        FreeSpinDecisionLogEnabled = FreeSpinDecisionLogEnabled,
        DoNotActivateInGames = DoNotActivateInGames,
        DarkMode = DarkMode,
        LastUpdateCheckUtc = LastUpdateCheckUtc,
    };
}
