using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmoothMice.App.ViewModels;
using SmoothMice.Core.Profiles;
using SmoothMice.Infrastructure.Persistence;
using SmoothMice.Infrastructure.Tray;
using SmoothMice.Infrastructure.Windows;

namespace SmoothMice.App;

public partial class App : Application
{
    private JsonSettingsRepository? _repo;
    private ProfileManager? _profiles;
    private ScrollCoordinator? _coordinator;
    private TrayIconService? _tray;
    private StartupRegistrationService? _startup;
    private MainViewModel? _vm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Keep process alive when the settings window is closed (hide-to-tray).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _repo = new JsonSettingsRepository();
        var loaded = _repo.LoadOrCreate();
        _profiles = new ProfileManager(loaded);

        var hook = new MouseHookService();
        var injector = new ScrollInjector();
        var apps = new ActiveAppResolver();
        _coordinator = new ScrollCoordinator(_profiles, hook, injector, apps);

        _startup = new StartupRegistrationService();
        _tray = new TrayIconService();
        _tray.OpenRequested += (_, _) =>
        {
            Current.Dispatcher.Invoke(() =>
            {
                if (MainWindow is { } w)
                {
                    w.Show();
                    w.WindowState = System.Windows.WindowState.Normal;
                    w.Activate();
                }
            });
        };
        _tray.ToggleEnableRequested += (_, _) =>
        {
            Current.Dispatcher.Invoke(() =>
            {
                if (_vm is null || _profiles is null || _repo is null || _coordinator is null || _startup is null)
                    return;
                _vm.Enabled = !_vm.Enabled;
                Persist();
                _tray.SetEnabledMenuText(_vm.Enabled);
            });
        };
        _tray.ExitRequested += (_, _) => Shutdown();

        _vm = new MainViewModel(_profiles, Persist);
        _tray.SetEnabledMenuText(_vm.Enabled);

        var main = new MainWindow { DataContext = _vm };
        main.Icon = CreateWindowIcon();
        MainWindow = main;
        main.Show();

        _coordinator.RefreshEnabledState();
    }

    private void Persist()
    {
        if (_vm is null || _profiles is null || _repo is null || _coordinator is null || _startup is null)
            return;

        try
        {
            _vm.Save();
            var snap = _profiles.Snapshot;
            _repo.Save(snap);

            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(exe))
                _startup.SetEnabled(snap.AutoStartOnLogin, exe);

            _coordinator.RefreshEnabledState();
            _tray?.SetEnabledMenuText(_vm.Enabled);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save settings: {ex.Message}", "SmoothMice", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_vm is not null && _profiles is not null && _repo is not null)
            {
                _vm.Save();
                _repo.Save(_profiles.Snapshot);
            }

            _coordinator?.Dispose();
            _tray?.Dispose();
        }
        finally
        {
            base.OnExit(e);
        }
    }

    internal void PersistFromUi() => Persist();

    private static ImageSource CreateWindowIcon()
    {
        var hbmp = IconFactory.CreateMouseHBitmap(64);
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hbmp, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            NativeMethods.DeleteObject(hbmp);
        }
    }
}
