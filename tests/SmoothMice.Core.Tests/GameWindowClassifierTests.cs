using SmoothMice.Infrastructure.Windows;
using Xunit;

namespace SmoothMice.Core.Tests;

public class GameWindowClassifierTests
{
    [Fact]
    public void Dying_light_the_beast_real_techland_window_is_a_game()
    {
        Assert.True(GameWindowClassifier.IsLikelyGame(
            targetClassName: null,
            rootClassName: "techland_game_class",
            executableName: "DyingLightGame_TheBeast_x64_rwdi.exe",
            rootIsForeground: true,
            rootIsFullscreenOrBorderless: true));
    }

    [Fact]
    public void Techland_class_without_foreground_or_fullscreen_fails_open_and_honors_exclusions()
    {
        Assert.False(GameWindowClassifier.IsLikelyGame(
            targetClassName: null,
            rootClassName: "techland_game_class",
            executableName: "DyingLightGame_TheBeast_x64_rwdi.exe",
            rootIsForeground: false,
            rootIsFullscreenOrBorderless: false));

        Assert.False(GameWindowClassifier.IsLikelyGame(
            targetClassName: null,
            rootClassName: "techland_game_class",
            executableName: "chrome.exe",
            rootIsForeground: true,
            rootIsFullscreenOrBorderless: true));
    }

    [Theory]
    [InlineData("UnityWndClass")]
    [InlineData("UnrealWindow")]
    [InlineData("SDL_app")]
    [InlineData("GLFW33")]
    [InlineData("GodotEngine")]
    public void Strong_engine_class_in_foreground_is_a_game(string className)
    {
        Assert.True(GameWindowClassifier.IsLikelyGame(
            targetClassName: null,
            rootClassName: className,
            executableName: "sample.exe",
            rootIsForeground: true,
            rootIsFullscreenOrBorderless: false));
    }

    [Fact]
    public void Godot_legacy_engine_class_accepts_windowed_foreground_or_fullscreen()
    {
        Assert.True(GameWindowClassifier.IsLikelyGame(
            targetClassName: null,
            rootClassName: "Engine",
            executableName: "godot-game.exe",
            rootIsForeground: true,
            rootIsFullscreenOrBorderless: false));

        Assert.True(GameWindowClassifier.IsLikelyGame(
            targetClassName: null,
            rootClassName: "Engine",
            executableName: "godot-game.exe",
            rootIsForeground: false,
            rootIsFullscreenOrBorderless: true));

        Assert.False(GameWindowClassifier.IsLikelyGame(
            targetClassName: null,
            rootClassName: "Engine",
            executableName: "godot-game.exe",
            rootIsForeground: false,
            rootIsFullscreenOrBorderless: false));
    }

    [Fact]
    public void Strong_engine_class_in_fullscreen_is_a_game_even_when_not_foreground()
    {
        Assert.True(GameWindowClassifier.IsLikelyGame(
            targetClassName: "UnityWndClass",
            rootClassName: "UnityWndClass",
            executableName: "sample.exe",
            rootIsForeground: false,
            rootIsFullscreenOrBorderless: true));
    }

    [Theory]
    [InlineData("chrome.exe", "UnityWndClass")]
    [InlineData("powerpnt.exe", "UnrealWindow")]
    [InlineData("steam.exe", "SDL_app")]
    [InlineData("chrome.exe", "Engine")]
    public void Explicitly_excluded_executables_are_never_games(string executableName, string className)
    {
        Assert.False(GameWindowClassifier.IsLikelyGame(
            targetClassName: className,
            rootClassName: className,
            executableName: executableName,
            rootIsForeground: true,
            rootIsFullscreenOrBorderless: true));
    }

    [Theory]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("PPTFrameClass")]
    [InlineData("WorkerW")]
    [InlineData("NotARecognizedEngine")]
    public void Weak_or_explicitly_excluded_classes_fail_open(string className)
    {
        Assert.False(GameWindowClassifier.IsLikelyGame(
            targetClassName: className,
            rootClassName: className,
            executableName: "sample.exe",
            rootIsForeground: true,
            rootIsFullscreenOrBorderless: true));
    }

    [Fact]
    public void Strong_engine_class_without_foreground_or_fullscreen_signal_fails_open()
    {
        Assert.False(GameWindowClassifier.IsLikelyGame(
            targetClassName: "UnityWndClass",
            rootClassName: "UnityWndClass",
            executableName: "sample.exe",
            rootIsForeground: false,
            rootIsFullscreenOrBorderless: false));
    }

    [Fact]
    public void Cache_policy_reuses_only_a_live_matching_root_within_its_short_ttl()
    {
        var root = new IntPtr(42);

        Assert.True(GameWindowCachePolicy.CanReuse(
            root, 100, cacheIsComplete: true, cachedAtMs: 1_000,
            root, 100, currentRootIsWindow: true, nowMs: 1_000 + GameWindowCachePolicy.TimeToLiveMs - 1));
        Assert.False(GameWindowCachePolicy.CanReuse(
            root, 100, cacheIsComplete: true, cachedAtMs: 1_000,
            root, 100, currentRootIsWindow: true, nowMs: 1_000 + GameWindowCachePolicy.TimeToLiveMs));
    }

    [Fact]
    public void Cache_policy_invalidates_handle_reuse_window_loss_and_failed_probes()
    {
        var root = new IntPtr(42);

        Assert.False(GameWindowCachePolicy.CanReuse(root, 100, true, 1_000, root, 101, true, 1_100));
        Assert.False(GameWindowCachePolicy.CanReuse(root, 100, true, 1_000, root, 100, false, 1_100));
        Assert.False(GameWindowCachePolicy.CanReuse(root, 100, false, 1_000, root, 100, true, 1_100));
    }
}
