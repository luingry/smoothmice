using SmoothMice.Core.Profiles;

namespace SmoothMice.Core.Config;

public static class DefaultSettings
{
    public const string GlobalProfileId   = "default";
    public const string GlobalProfileName = "Default (All Applications)";

    public static ScrollProfileSettings CreateGlobalProfileSettings() => new()
    {
        StepSizePx      = 80,
        AnimationTimeMs = 150,
        TailToHeadRatio = 3,
        AnimationEasing = true,

        AccelerationDeltaMs    = 400,
        AccelerationCurvePreset = 1,    // Smooth
        AccelerationExponent   = 1.3,
        AccelerationMaxX       = 3.5,

        EnableForAllAppsByDefault = true,
        HorizontalSmoothness      = true,
    };

    public static AppSettings CreateAppSettings() => new()
    {
        SchemaVersion     = 1,
        AutoStartOnLogin  = true,
        Enabled           = true,
        SelectedProfileId = GlobalProfileId,
        Profiles =
        [
            new ScrollProfile
            {
                Id             = GlobalProfileId,
                DisplayName    = GlobalProfileName,
                ExecutableName = null,
                IsGlobal       = true,
                Settings       = CreateGlobalProfileSettings(),
            },
        ],
    };
}
