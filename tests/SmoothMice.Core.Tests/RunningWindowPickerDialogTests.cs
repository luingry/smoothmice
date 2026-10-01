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

    [Fact]
    public void Running_windows_are_deduplicated_by_executable_preferring_a_titled_window()
    {
        var windows = new[]
        {
            new RunningApplicationWindow(new IntPtr(1), 10, "chrome.exe", string.Empty),
            new RunningApplicationWindow(new IntPtr(2), 11, "Chrome.EXE", "Inbox"),
            new RunningApplicationWindow(new IntPtr(3), 12, "chrome.exe", "Docs"),
            new RunningApplicationWindow(new IntPtr(4), 20, "notepad.exe", "notes.txt"),
        };

        var distinct = RunningApplicationWindow.DistinctByExecutable(windows);

        Assert.Equal(2, distinct.Count);
        Assert.Equal(new IntPtr(2), distinct[0].Hwnd);
        Assert.Equal(new IntPtr(4), distinct[1].Hwnd);
    }
}
