using System.IO;
using System.Windows;
using SwissTechKnife.Sdk;

namespace SmoothMice.Module;

/// <summary>
/// SwissTechKnife entry point. SmoothMice keeps its own process and window; the module starts it with the host,
/// restarts it after a crash, stops it when disabled and opens its window from the module page.
/// </summary>
public sealed class SmoothMiceModule : IBackgroundModule
{
    /// <summary>Folder (inside the package) holding the app build; see scripts/pack-module.ps1.</summary>
    internal const string AppFolder = "app";
    internal const string AppExecutable = "SmoothMice.Hosted.exe";

    private IModuleContext? _context;
    private HostedAppSupervisor? _supervisor;

    public void Initialize(IModuleContext context)
    {
        _context = context;
        _supervisor = new HostedAppSupervisor(
            Path.Combine(context.InstallDirectory, AppFolder, AppExecutable),
            context.DataDirectory,
            context.Log);
    }

    public void Start() => _supervisor?.Start();

    public void Stop() => _supervisor?.Stop();

    public FrameworkElement CreateView() => new ModulePage(_supervisor!, _context!);

    public void Dispose()
    {
        _supervisor?.Dispose();
        _supervisor = null;
    }
}
