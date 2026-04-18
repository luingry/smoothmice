using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SmoothMice.App.ViewModels;

namespace SmoothMice.App;

public partial class MainWindow
{
    private bool _profileSuppress;
    private bool _presetSuppress;
    private bool _loaded;
    private bool _namesEventsWired;
    private bool _clientSizeSnapped;

    public IReadOnlyList<string> AccelPresetNames => MainViewModel.AccelPresetNames;

    public MainWindow()
    {
        InitializeComponent();
        VersionLabel.Text = FormatAppVersion();
        Deactivated += MainWindow_OnDeactivated;
    }

    private static string FormatAppVersion()
    {
        var asm = typeof(MainWindow).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "";
    }

    private void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Hide to tray instead of closing; actual exit goes through tray "Exit"
        e.Cancel = true;
        ((App)Application.Current).PersistFromUi();
        Hide();
    }

    private void MainWindow_OnDeactivated(object? sender, EventArgs e)
    {
        if (!_loaded) return;
        Keyboard.ClearFocus();
        ((App)Application.Current).PersistFromUi();
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (!_namesEventsWired)
        {
            vm.ProfileNames.CollectionChanged += (_, _) =>
            {
                _profileSuppress = true;
                ProfileCombo.SelectedItem = vm.SelectedProfile?.DisplayName;
                _profileSuppress = false;
            };
            _namesEventsWired = true;
        }

        _profileSuppress = true;
        ProfileCombo.ItemsSource  = vm.ProfileNames;
        ProfileCombo.SelectedItem = vm.SelectedProfile?.DisplayName;
        _profileSuppress = false;

        SyncPresetCombo(vm);
        _loaded = true;
    }

    private void ProfileCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _profileSuppress || DataContext is not MainViewModel vm) return;
        if (ProfileCombo.SelectedItem is not string name) return;

        _profileSuppress = true;
        try
        {
            vm.SwitchProfile(name);
            ProfileCombo.SelectedItem = vm.SelectedProfile?.DisplayName;
            SyncPresetCombo(vm);
        }
        finally { _profileSuppress = false; }
    }

    private void AccelPreset_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _presetSuppress || DataContext is not MainViewModel vm) return;
        if (AccelPresetCombo.SelectedIndex < 0) return;

        vm.AccelerationCurvePreset = AccelPresetCombo.SelectedIndex;
        ((App)Application.Current).PersistFromUi();
    }

    private void SyncPresetCombo(MainViewModel vm)
    {
        _presetSuppress = true;
        AccelPresetCombo.SelectedIndex = vm.AccelerationCurvePreset;
        _presetSuppress = false;
    }

    private void PersistCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        ((App)Application.Current).PersistFromUi();
    }

    private void UpdateFreqCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        ((App)Application.Current).PersistFromUi();
    }

    private void Numeric_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        ((App)Application.Current).PersistFromUi();
    }

    private void Help_OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "SmoothMice — smooth mouse wheel scrolling for Windows.\n\n" +
            "Acceleration uses an EWMA (exponentially-weighted moving average)\n" +
            "of scroll speed, so the multiplier ramps up/down smoothly with\n" +
            "no stepping artefacts.\n\n" +
            "Preset curves:\n" +
            "  Linear      (exp 1.0, max 2.5×) — proportional, gentle\n" +
            "  Smooth      (exp 1.3, max 3.5×) — Mac-like, default\n" +
            "  Exponential (exp 2.0, max 6.0×) — aggressive burst",
            "SmoothMice",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// WPF: SizeToContent width+height with non-resizable chrome can leave a black strip at the client edge
    /// (HWND vs renderer misalignment). Snap outer size to whole device pixels and stop auto-sizing.
    /// https://github.com/dotnet/wpf/issues/9816
    /// </summary>
    private void MainWindow_OnContentRendered(object? sender, EventArgs e)
    {
        if (_clientSizeSnapped)
            return;

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => MainWindow_OnContentRendered(sender, e)));
            return;
        }

        _clientSizeSnapped = true;

        var dpi = VisualTreeHelper.GetDpi(this);
        var physW = ActualWidth * dpi.DpiScaleX;
        var physH = ActualHeight * dpi.DpiScaleY;
        var snappedW = Math.Ceiling(physW) / dpi.DpiScaleX;
        var snappedH = Math.Ceiling(physH) / dpi.DpiScaleY;

        SizeToContent = SizeToContent.Manual;
        Width = snappedW;
        Height = snappedH;
    }
}
