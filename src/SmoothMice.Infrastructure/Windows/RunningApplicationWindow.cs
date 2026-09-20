namespace SmoothMice.Infrastructure.Windows;

/// <summary>A visible top-level window and the executable currently hosting it.</summary>
public sealed class RunningApplicationWindow
{
    public RunningApplicationWindow(IntPtr hwnd, uint processId, string executableName, string title)
    {
        Hwnd = hwnd;
        ProcessId = processId;
        ExecutableName = executableName;
        Title = title;
    }

    public IntPtr Hwnd { get; }
    public uint ProcessId { get; }
    public string ExecutableName { get; }
    public string Title { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Title)
        ? $"{ExecutableName} (PID {ProcessId})"
        : $"{Title} — {ExecutableName} (PID {ProcessId})";
}
