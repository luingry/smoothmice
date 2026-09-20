using SmoothMice.Infrastructure.Windows;
using Xunit;

namespace SmoothMice.Core.Tests;

public class RunningWindowPickerDialogTests
{
    [Fact]
    public void Running_window_display_name_contains_title_executable_and_process_id()
    {
        var candidate = new RunningApplicationWindow(
            new IntPtr(123), 456, "Example.exe", "Example window");

        Assert.Equal("Example window — Example.exe (PID 456)", candidate.DisplayName);
    }

    [Fact]
    public void Untitled_running_window_display_name_retains_executable_and_process_id()
    {
        var candidate = new RunningApplicationWindow(
            new IntPtr(123), 456, "Example.exe", string.Empty);

        Assert.Equal("Example.exe (PID 456)", candidate.DisplayName);
    }
}
