using Microsoft.Win32;

namespace SmoothMice.Infrastructure.Windows;

public sealed class StartupRegistrationService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SmoothMice";

    /// <summary>Command-line flag: start hidden (notification area only).</summary>
    public const string TrayStartupArg = "/tray";

    /// <summary>After OTA Inno finishes: one normal-window launch so layout runs, then app relaunches with <see cref="TrayStartupArg"/>.</summary>
    public const string PostOtaRelaunchArg = "/postota";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        var v = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(v);
    }

    /// <summary>True if <see cref="TrayStartupArg"/> appears in process command line.</summary>
    public static bool TrayStartupMatches(string[] args)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, TrayStartupArg, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool PostOtaRelaunchMatches(string[] args)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, PostOtaRelaunchArg, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? throw new InvalidOperationException("Cannot open Run registry key.");

        if (enabled)
            key.SetValue(ValueName, $"\"{executablePath}\" {TrayStartupArg}");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
