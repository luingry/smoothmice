using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SmoothMice.App.ViewModels;
using SmoothMice.Core.Profiles;
using SmoothMice.Core.Updates;
using SmoothMice.Infrastructure.Persistence;
using SmoothMice.Infrastructure.Tray;
using SmoothMice.Infrastructure.Updates;
using SmoothMice.Infrastructure.Windows;

namespace SmoothMice.App;

public partial class App : Application
{
    /// <summary>Written by the OTA install batch if Inno Setup returns non-zero; shown on next startup.</summary>
    private static string LastOtaSetupErrorPath() =>
        Path.Combine(Path.GetTempPath(), "SmoothMiceLastOtaSetupError.txt");

    private JsonSettingsRepository? _repo;
    private ProfileManager? _profiles;
    private ScrollCoordinator? _coordinator;
    private TrayIconService? _tray;
    private StartupRegistrationService? _startup;
    private MainViewModel? _vm;
    private GitHubReleaseUpdateChecker? _updateChecker;
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Keep process alive when the settings window is closed (hide-to-tray).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _repo = new JsonSettingsRepository();
        var loaded = _repo.LoadOrCreate();
        _profiles = new ProfileManager(loaded);

        TryNotifyOtaInstallFailureFromLastRun();

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
                if (MainWindow is MainWindow w)
                {
                    w.Show();
                    w.WindowState = System.Windows.WindowState.Normal;
                    w.RecalculateWindowSize();
                    w.Activate();
                }
            });
        };
        _tray.ToggleEnableRequested += (_, _) =>
        {
            Current.Dispatcher.Invoke(() =>
            {
                if (_profiles is null) return;
                var snap = _profiles.Snapshot;
                var global = snap.Profiles.FirstOrDefault(p => p.IsGlobal);
                if (global is null) return;
                global.Settings.Enabled = !global.Settings.Enabled;
                _profiles.UpsertEditedProfile(global);
                Persist();
            });
        };
        _tray.ExitRequested += (_, _) => Shutdown();

        _updateChecker = new GitHubReleaseUpdateChecker();
        _vm = new MainViewModel(_profiles, Persist, () => { _ = CheckForUpdatesAsync(manual: true); });
        _tray.SetEnabledMenuText(_profiles.Snapshot.Profiles.FirstOrDefault(p => p.IsGlobal)?.Settings.Enabled ?? true);

        var postOta = StartupRegistrationService.PostOtaRelaunchMatches(e.Args);
        var startInTray = StartupRegistrationService.TrayStartupMatches(e.Args) && !postOta;

        var main = new MainWindow { DataContext = _vm };
        main.Icon = CreateWindowIcon();
        MainWindow = main;

        if (postOta && main is MainWindow mwPost)
        {
            void OnPostOtaFirstRender(object? s, EventArgs ev)
            {
                mwPost.ContentRendered -= OnPostOtaFirstRender;
                mwPost.RecalculateWindowSize();
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, PostOtaRelaunchToTray);
            }

            mwPost.ContentRendered += OnPostOtaFirstRender;
        }

        if (startInTray)
        {
            main.ShowInTaskbar = false;
            main.WindowState = WindowState.Minimized;
            main.Show();
            main.Hide();
            main.ShowInTaskbar = true;
        }
        else
        {
            main.Show();
        }

        // Defer hook installation to Normal priority so the pump is running when
        // SetWindowsHookEx is called.  Normal priority fires before the first render pass
        // (ContentRendered fires at Render priority, which is lower), meaning the brief
        // input-chain pause from SetWindowsHookEx happens while the window is still invisible
        // — completely imperceptible to the user.
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            _coordinator?.RefreshEnabledState();
        }));

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (postOta)
                    return;
                if (_profiles?.Snapshot is not { } snap)
                    return;
                if (!ShouldRunScheduledUpdateCheck(snap))
                    return;
                _ = CheckForUpdatesAsync(manual: false);
            }));
    }

    private void PostOtaRelaunchToTray()
    {
        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exe))
        {
            MessageBox.Show(
                "Could not locate the SmoothMice executable to finish the post-update restart.\n\n" +
                "Close this window and start SmoothMice from the Start menu.",
                "SmoothMice",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = StartupRegistrationService.TrayStartupArg,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "SmoothMice could not restart in the notification area after the update.\n\n" +
                $"{ex.Message}\n\n" +
                "Close this window and start SmoothMice from the Start menu if needed.",
                "SmoothMice",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Shutdown(0);
    }

    private static void TryNotifyOtaInstallFailureFromLastRun()
    {
        var path = LastOtaSetupErrorPath();
        string? text;
        try
        {
            if (!File.Exists(path))
                return;
            text = File.ReadAllText(path).Trim();
            File.Delete(path);
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
            return;

        MessageBox.Show(
            "The last in-app update did not finish successfully (Inno Setup reported a failure).\n\n" +
            $"{text}\n\n" +
            "Download the latest installer from the release page and run it manually if the app misbehaves.",
            "SmoothMice — update",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static bool ShouldRunScheduledUpdateCheck(AppSettings s) =>
        s.UpdateCheckFrequency switch
        {
            UpdateCheckFrequency.Never => false,
            UpdateCheckFrequency.DailyOnStartup => true,
            UpdateCheckFrequency.Weekly =>
                s.LastUpdateCheckUtc is not { } last
                || DateTimeOffset.UtcNow - last >= TimeSpan.FromDays(7),
            UpdateCheckFrequency.Monthly =>
                s.LastUpdateCheckUtc is not { } lastM
                || DateTimeOffset.UtcNow - lastM >= TimeSpan.FromDays(30),
            _ => false,
        };

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_profiles is null || _repo is null || _updateChecker is null)
            return;

        if (!await _updateCheckGate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            var current = TryGetCurrentAppVersion();
            var result = await _updateChecker
                .QueryLatestAsync(current, CancellationToken.None)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                if (manual)
                {
                    Dispatcher.Invoke(() => MessageBox.Show(
                        $"Could not check for updates.\n\n{result.ErrorMessage}",
                        "SmoothMice",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
                }

                return;
            }

            _profiles.SetLastSuccessfulUpdateCheckUtc(DateTimeOffset.UtcNow);
            _repo.Save(_profiles.Snapshot);

            if (result.UpdateAvailable)
            {
                var wantsInstall = false;
                Dispatcher.Invoke(() =>
                {
                    if (string.IsNullOrWhiteSpace(result.InstallerDownloadUrl) ||
                        string.IsNullOrWhiteSpace(result.InstallerAssetName))
                    {
                        var open = MessageBox.Show(
                            $"Version {result.LatestVersionLabel} is available, but the installer " +
                            "(SmoothMice_Setup_*.exe) was not found in this release's assets on GitHub.\n\n" +
                            "Open the release page?",
                            "SmoothMice — update",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);
                        if (open == MessageBoxResult.Yes && result.ReleasePageUrl is { } page)
                            OpenUrlInBrowser(page);
                        return;
                    }

                    var go = MessageBox.Show(
                        $"Version {result.LatestVersionLabel} is available.\n\n" +
                        "Download and install now? Setup will run silently (Inno Setup); " +
                        "the app will close during installation and try to reopen afterward.",
                        "SmoothMice — update",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    wantsInstall = go == MessageBoxResult.Yes;
                });

                if (!wantsInstall)
                    return;

                try
                {
                    try
                    {
                        File.Delete(LastOtaSetupErrorPath());
                    }
                    catch
                    {
                        // ignore
                    }

                    var workDir = Path.Combine(Path.GetTempPath(), "SmoothMiceUpdate", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(workDir);
                    var setupPath = Path.Combine(workDir, result.InstallerAssetName!);

                    Dispatcher.Invoke(() => _vm?.StartUpdateBanner("Downloading update…"));
                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        Dispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(() => _vm?.ReportDownloadProgress(p)));
                    });

                    await GitHubReleaseUpdateChecker
                        .DownloadInstallerToFileAsync(
                            result.InstallerDownloadUrl!,
                            setupPath,
                            SmoothMiceHttpUserAgent(),
                            progress,
                            CancellationToken.None)
                        .ConfigureAwait(false);

                    Dispatcher.Invoke(() => _vm?.SetUpdateBannerInstalling());

                    var installedExe = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Programs", "SmoothMice", "SmoothMice.exe");

                    var errFlag = LastOtaSetupErrorPath();
                    var batPath = Path.Combine(workDir, "_run_install.bat");
                    // Wait for exit, then taskkill (best-effort) so Inno can replace SmoothMice.exe; /CLOSEAPPLICATIONS as backup.
                    // After install: /postota = one normal layout pass, then app relaunches with /tray (fixes bad WPF size after OTA).
                    var bat = "@echo off\r\n" +
                              "timeout /t 5 /nobreak >nul\r\n" +
                              "taskkill /IM SmoothMice.exe /F >nul 2>&1\r\n" +
                              "timeout /t 2 /nobreak >nul\r\n" +
                              $"start /wait \"\" \"{EscapeForBatchPath(setupPath)}\" /VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /CLOSEAPPLICATIONS\r\n" +
                              $"if errorlevel 1 echo OTA_SETUP_FAILED code %%ERRORLEVEL%% > \"{EscapeForBatchPath(errFlag)}\"\r\n" +
                              $"if exist \"{EscapeForBatchPath(installedExe)}\" start \"\" \"{EscapeForBatchPath(installedExe)}\" {StartupRegistrationService.PostOtaRelaunchArg}\r\n" +
                              "del \"%~f0\"\r\n";

                    File.WriteAllText(batPath, bat);

                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            _vm?.Save();
                            _repo.Save(_profiles.Snapshot);
                        }
                        catch
                        {
                            // best effort before shutdown
                        }

                        try
                        {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c \"{batPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                        };
                            Process.Start(psi);
                        }
                        catch (Exception ex)
                        {
                            _vm?.EndUpdateBanner();
                            MessageBox.Show(
                                $"Could not start the installation.\n\n{ex.Message}",
                                "SmoothMice",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            return;
                        }

                        _vm?.EndUpdateBanner();
                        Shutdown(0);
                    });
                }
                catch (Exception ex)
                {
                    // User already agreed to install — always tell them if download / launcher prep failed
                    // (scheduled checks use manual=false but still show the install prompt).
                    Dispatcher.Invoke(() =>
                    {
                        _vm?.EndUpdateBanner();
                        MessageBox.Show(
                            $"Failed to download or prepare the update.\n\n{ex.Message}",
                            "SmoothMice",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    });
                }
            }
            else if (manual)
            {
                Dispatcher.Invoke(() => MessageBox.Show(
                    "You are using the latest version.",
                    "SmoothMice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information));
            }
        }
        finally
        {
            _updateCheckGate.Release();
        }
    }

    private static string SmoothMiceHttpUserAgent()
    {
        var v = TryGetCurrentAppVersion()?.ToString(3) ?? "0";
        return $"SmoothMice/{v} (+https://github.com/luingry/smoothmice)";
    }

    /// <summary>Duplica aspas para uso dentro de linhas batch entre aspas duplas.</summary>
    private static string EscapeForBatchPath(string path) =>
        path.Replace("\"", "\"\"");

    private static Version? TryGetCurrentAppVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return GitHubReleaseUpdateChecker.ParseVersionLoose(info)
               ?? (asm.GetName().Version is { } v ? GitHubReleaseUpdateChecker.NormalizeVersion(v) : null);
    }

    private static void OpenUrlInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
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
            var globalEnabled = snap.Profiles.FirstOrDefault(p => p.IsGlobal)?.Settings.Enabled ?? true;
            _tray?.SetEnabledMenuText(globalEnabled);
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
            _updateChecker?.Dispose();
            _updateCheckGate.Dispose();
        }
        finally
        {
            base.OnExit(e);
        }
    }

    internal void PersistFromUi() => Persist();

    private static ImageSource CreateWindowIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/SmoothMice.ico", UriKind.Absolute);
            if (Application.GetResourceStream(uri) is { } res)
            {
                using (res.Stream)
                    return BitmapFrame.Create(
                        res.Stream,
                        BitmapCreateOptions.None,
                        BitmapCacheOption.OnLoad);
            }
        }
        catch (Exception)
        {
            // fall back to generated bitmap
        }

        var hbmp = IconFactory.CreateMouseHBitmap(64);
        try
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hbmp, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            NativeMethods.DeleteObject(hbmp);
        }
    }
}
