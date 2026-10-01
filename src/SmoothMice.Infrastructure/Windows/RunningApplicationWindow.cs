namespace SmoothMice.Infrastructure.Windows;

/// <summary>A visible top-level window and the executable currently hosting it.</summary>
public sealed class RunningApplicationWindow
{
    public RunningApplicationWindow(IntPtr hwnd, uint processId, string executableName, string title,
        string? executablePath = null)
    {
        Hwnd = hwnd;
        ProcessId = processId;
        ExecutableName = executableName;
        Title = title;
        ExecutablePath = executablePath ?? string.Empty;
    }

    public IntPtr Hwnd { get; }
    public uint ProcessId { get; }
    public string ExecutableName { get; }
    public string Title { get; }
    public string ExecutablePath { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Title)
        ? $"{ExecutableName} (PID {ProcessId})"
        : $"{Title} — {ExecutableName} (PID {ProcessId})";

    /// <summary>
    /// Keeps one window per executable, since profiles are keyed by executable name. The first
    /// titled window in the given (Z-)order wins; an untitled one is kept only when the
    /// executable has no titled window.
    /// </summary>
    public static IReadOnlyList<RunningApplicationWindow> DistinctByExecutable(
        IEnumerable<RunningApplicationWindow> windows)
    {
        var byExecutable = new Dictionary<string, RunningApplicationWindow>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var window in windows)
        {
            if (!byExecutable.TryGetValue(window.ExecutableName, out var kept))
            {
                byExecutable[window.ExecutableName] = window;
                order.Add(window.ExecutableName);
            }
            else if (string.IsNullOrWhiteSpace(kept.Title) && !string.IsNullOrWhiteSpace(window.Title))
            {
                byExecutable[window.ExecutableName] = window;
            }
        }

        return order.Select(name => byExecutable[name]).ToArray();
    }
}
