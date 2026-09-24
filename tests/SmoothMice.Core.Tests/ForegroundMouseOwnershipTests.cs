using SmoothMice.Infrastructure.Windows;
using Xunit;

namespace SmoothMice.Core.Tests;

public class ForegroundMouseOwnershipTests
{
    [Fact]
    public void Hidden_cursor_of_a_fullscreen_game_drifting_onto_another_monitor_routes_to_the_game()
    {
        // Real repro: Dying Light: The Beast borderless 2560x1080, cursor hidden, cursor over Orca
        // on the second monitor, no capture and no ClipCursor.
        Assert.True(ForegroundMouseOwnership.ShouldRouteToForeground(
            targetIsForegroundRoot: false, cursorIsHidden: true, foregroundIsFullscreen: true));
    }

    [Fact]
    public void Wheel_over_the_foreground_window_itself_keeps_normal_handling()
    {
        Assert.False(ForegroundMouseOwnership.ShouldRouteToForeground(
            targetIsForegroundRoot: true, cursorIsHidden: true, foregroundIsFullscreen: true));
    }

    [Theory]
    [InlineData(false, true)]  // visible pointer: the user deliberately hovers the other monitor
    [InlineData(true,  false)] // pointer hidden while typing in a normal window: keep hover scroll
    public void Hover_scroll_over_a_background_window_is_preserved(bool hidden, bool fullscreen)
    {
        Assert.False(ForegroundMouseOwnership.ShouldRouteToForeground(
            targetIsForegroundRoot: false, cursorIsHidden: hidden, foregroundIsFullscreen: fullscreen));
    }
}
