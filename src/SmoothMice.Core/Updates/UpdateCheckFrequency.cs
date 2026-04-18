namespace SmoothMice.Core.Updates;

/// <summary>How often the app should contact GitHub for a newer release.</summary>
public enum UpdateCheckFrequency
{
    /// <summary>Check once every time the process starts.</summary>
    DailyOnStartup = 0,

    /// <summary>Check on startup if the last successful check was 7+ days ago.</summary>
    Weekly = 1,

    /// <summary>Check on startup if the last successful check was 30+ days ago.</summary>
    Monthly = 2,

    Never = 3,
}
