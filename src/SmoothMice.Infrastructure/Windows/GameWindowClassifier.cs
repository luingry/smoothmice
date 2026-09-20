namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Conservative, pure game-window classifier.
///
/// A positive result requires a known engine window class plus either foreground ownership or a
/// fullscreen/borderless root window. Godot's legacy <c>Engine</c> class follows the same
/// foreground-or-fullscreen rule, while explicit exclusions still take precedence. Executable names are used only for explicit
/// exclusions; an executable name alone never identifies a game. Unknown or incomplete signals
/// are negative so physical wheel input continues natively (fail-open).
/// </summary>
public static class GameWindowClassifier
{
    private static readonly HashSet<string> ExcludedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        // Browsers
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe",
        // Media players
        "vlc.exe", "mpv.exe", "potplayermini64.exe", "wmplayer.exe", "spotify.exe",
        // Presentation / desktop shell
        "powerpnt.exe", "explorer.exe", "applicationframehost.exe", "shellexperiencehost.exe",
        "startmenuexperiencehost.exe", "searchhost.exe",
        // Launchers are not games, even when they use a graphics framework.
        "steam.exe", "epicgameslauncher.exe", "galaxyclient.exe", "battle.net.exe",
        "riotclientservices.exe", "ubisoftconnect.exe",
    };

    private static readonly HashSet<string> ExcludedWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chrome_WidgetWin_1", "Chrome_RenderWidgetHostHWND", "MozillaWindowClass",
        "PPTFrameClass", "screen", "Progman", "WorkerW", "Shell_TrayWnd", "CabinetWClass",
        "ApplicationFrameWindow",
    };

    private static readonly HashSet<string> StrongEngineClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnityWndClass", "UnrealWindow", "SDL_app", "Godot", "GodotEngine", "techland_game_class",
    };

    /// <summary>
    /// Classifies only strong, documented application-window signals. Godot's broad legacy
    /// <c>Engine</c> class is allowed only with the same root-window signals as other engines.
    /// </summary>
    public static bool IsLikelyGame(
        string? targetClassName,
        string? rootClassName,
        string? executableName,
        bool rootIsForeground,
        bool rootIsFullscreenOrBorderless)
    {
        if (IsExcluded(executableName, targetClassName) || IsExcluded(executableName, rootClassName))
            return false;

        if (IsGodotLegacyEngineClass(targetClassName) || IsGodotLegacyEngineClass(rootClassName))
            return rootIsForeground || rootIsFullscreenOrBorderless;

        if (!IsStrongEngineClass(targetClassName) && !IsStrongEngineClass(rootClassName))
            return false;

        // A real game is normally foreground. Fullscreen/borderless coverage is an independent
        // strong signal that also supports native hover-scroll behavior for a game window.
        return rootIsForeground || rootIsFullscreenOrBorderless;
    }

    private static bool IsExcluded(string? executableName, string? className) =>
        IsMember(ExcludedExecutables, executableName) || IsMember(ExcludedWindowClasses, className);

    private static bool IsStrongEngineClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return false;

        var value = className!;
        return StrongEngineClasses.Contains(value) ||
               value.StartsWith("GLFW", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGodotLegacyEngineClass(string? className) =>
        string.Equals(className, "Engine", StringComparison.OrdinalIgnoreCase);

    private static bool IsMember(HashSet<string> values, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return values.Contains(value!);
    }
}
