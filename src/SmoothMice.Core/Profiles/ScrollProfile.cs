using SmoothMice.Core.Config;

namespace SmoothMice.Core.Profiles;

public sealed class ScrollProfile
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Executable file name only, e.g. chrome.exe. Null for global profile.</summary>
    public string? ExecutableName { get; set; }
    public bool IsGlobal { get; set; }
    public ScrollProfileSettings Settings { get; set; } = new();

    public ScrollProfile Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        ExecutableName = ExecutableName,
        IsGlobal = IsGlobal,
        Settings = Settings.Clone(),
    };
}
