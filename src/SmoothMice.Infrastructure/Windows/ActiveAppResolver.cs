using System.Diagnostics;
using System.IO;

namespace SmoothMice.Infrastructure.Windows;

public sealed class ActiveAppResolver
{
    public string? TryGetForegroundExecutablePath()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return null;

        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    public static string? ExecutableNameFromPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;
        try
        {
            return Path.GetFileName(fullPath);
        }
        catch
        {
            return null;
        }
    }
}
