using SmoothMice.Core.Config;
using SmoothMice.Core.Diagnostics;
using SmoothMice.Core.Updates;

namespace SmoothMice.Core.Profiles;

public sealed class AppSettings
{
    private int _freeSpinLiftTarget = 40;
    private int _freeSpinLandingTarget = 40;
    private int _freeSpinRepositionTarget = 40;
    private int _freeSpinLegitimateScrollTarget = 40;
    public int SchemaVersion { get; set; } = 1;
    public bool AutoStartOnLogin { get; set; } = true;
    public string SelectedProfileId { get; set; } = DefaultSettings.GlobalProfileId;
    public List<ScrollProfile> Profiles { get; set; } = [];

    public UpdateCheckFrequency UpdateCheckFrequency { get; set; } = UpdateCheckFrequency.DailyOnStartup;

    /// <summary>
    /// Enables the Free-Spin Inertia Suppression module surface. The current rollout is monitor-only
    /// and deliberately does not alter the scroll input path.
    /// </summary>
    public bool FreeSpinInertiaSuppressionEnabled { get; set; }

    /// <summary>Read-only is deliberately the compatibility default for existing installs.</summary>
    public FreeSpinDetectionMode FreeSpinDetectionMode { get; set; } = FreeSpinDetectionMode.ReadOnly;
    private int _freeSpinSuppressionConfidenceThreshold = 90;
    /// <summary>Minimum conservative (not generic probability) confidence required to suppress.</summary>
    public int FreeSpinSuppressionConfidenceThreshold { get => _freeSpinSuppressionConfidenceThreshold; set => _freeSpinSuppressionConfidenceThreshold = ClampFreeSpinConfidenceThreshold(value); }

    /// <summary>
    /// When enabled, physical wheel input is left untouched for conservatively identified game windows.
    /// This is a global shell preference; it is intentionally not an application profile setting.
    /// </summary>
    public bool DoNotActivateInGames { get; set; }

    /// <summary>Uses the application's neutral dark palette instead of the default light palette.</summary>
    public bool DarkMode { get; set; }

    public int FreeSpinLiftTarget { get => _freeSpinLiftTarget; set => _freeSpinLiftTarget = ClampCalibrationTarget(value); }
    public int FreeSpinLandingTarget { get => _freeSpinLandingTarget; set => _freeSpinLandingTarget = ClampCalibrationTarget(value); }
    public int FreeSpinRepositionTarget { get => _freeSpinRepositionTarget; set => _freeSpinRepositionTarget = ClampCalibrationTarget(value); }
    public int FreeSpinLegitimateScrollTarget { get => _freeSpinLegitimateScrollTarget; set => _freeSpinLegitimateScrollTarget = ClampCalibrationTarget(value); }

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
        FreeSpinSuppressionConfidenceThreshold = FreeSpinSuppressionConfidenceThreshold,
        DoNotActivateInGames = DoNotActivateInGames,
        DarkMode = DarkMode,
        FreeSpinLiftTarget = FreeSpinLiftTarget,
        FreeSpinLandingTarget = FreeSpinLandingTarget,
        FreeSpinRepositionTarget = FreeSpinRepositionTarget,
        FreeSpinLegitimateScrollTarget = FreeSpinLegitimateScrollTarget,
        LastUpdateCheckUtc = LastUpdateCheckUtc,
    };

    public static int ClampCalibrationTarget(int value) => Math.Max(30, Math.Min(50, value));
    public static int ClampFreeSpinConfidenceThreshold(int value) => Math.Max(50, Math.Min(99, value));
}
