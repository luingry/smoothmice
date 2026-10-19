using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SmoothMice.App;

/// <summary>
/// Options for running under the SwissTechKnife host (<c>--hosted</c>). The host's SmoothMice module
/// launches this process and owns tray, updates and auto-start, so hosted mode turns those off,
/// keeps settings in the module's data folder and exits with its parent. Scrolling itself is unchanged.
/// </summary>
public sealed class HostedMode : IDisposable
{
    public const string HostedArg = "--hosted";
    public const string ParentArg = "--parent";
    public const string DataArg = "--data";
    public const string ShowArg = "--show";

    /// <summary>Exit code when another SmoothMice (standalone) already owns the single-instance mutex.</summary>
    public const int AlreadyRunningExitCode = 3;

    /// <summary>Set by the module to ask the hosted process to save and exit.</summary>
    public const string ExitEventName = @"Local\SmoothMice.Hosted.Exit";

    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitRegistration;
    private Process? _parent;

    private HostedMode(int parentId, string dataDirectory, bool show)
    {
        ParentId = parentId;
        DataDirectory = dataDirectory;
        ShowOnStart = show;
    }

    public int ParentId { get; }
    public string DataDirectory { get; }
    public bool ShowOnStart { get; }

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    /// <summary>Null unless the command line carries <c>--hosted</c> with a valid <c>--data</c> folder.</summary>
    public static HostedMode? TryParse(string[] args)
    {
        var hosted = false;
        var show = false;
        var parent = 0;
        string? data = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, HostedArg, StringComparison.OrdinalIgnoreCase))
                hosted = true;
            else if (string.Equals(arg, ShowArg, StringComparison.OrdinalIgnoreCase))
                show = true;
            else if (string.Equals(arg, ParentArg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                int.TryParse(args[++i], out parent);
            else if (string.Equals(arg, DataArg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                data = args[++i];
        }

        if (!hosted || string.IsNullOrWhiteSpace(data) || !Path.IsPathRooted(data))
            return null;
        return new HostedMode(parent, data!, show);
    }

    /// <summary>
    /// First hosted run: copies the standalone settings (and their backup) so the user keeps their profiles.
    /// </summary>
    public void MigrateStandaloneSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return;
            Directory.CreateDirectory(DataDirectory);
            var standalone = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmoothMice", "settings.json");
            if (File.Exists(standalone))
                File.Copy(standalone, SettingsPath);
            if (File.Exists(standalone + ".bak") && !File.Exists(SettingsPath + ".bak"))
                File.Copy(standalone + ".bak", SettingsPath + ".bak");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Defaults are created instead; the standalone file is never modified.
        }
    }

    /// <summary>Calls <paramref name="exit"/> (from any thread) when the host exits or asks this process to exit.</summary>
    public void WatchForExit(Action exit)
    {
        _exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName);
        _exitRegistration = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => exit(), null, Timeout.Infinite, executeOnlyOnce: true);

        if (ParentId <= 0)
            return;
        try
        {
            _parent = Process.GetProcessById(ParentId);
            _parent.EnableRaisingEvents = true;
            _parent.Exited += (_, _) => exit();
            if (_parent.HasExited)
                exit();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The parent is already gone.
            exit();
        }
    }

    public void Dispose()
    {
        _exitRegistration?.Unregister(null);
        _exitEvent?.Dispose();
        _parent?.Dispose();
    }
}
