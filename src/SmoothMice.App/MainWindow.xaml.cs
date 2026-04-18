using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SmoothMice.App.ViewModels;

namespace SmoothMice.App;

public partial class MainWindow
{
    private bool _profileSuppress;
    private bool _presetSuppress;
    private bool _loaded;
    private bool _namesEventsWired;
    private MainViewModel? _vmSubscribed;
    private int _snapRetryRemaining = 40;
    private DispatcherTimer? _liveApplyTimer;

    public MainWindow()
    {
        InitializeComponent();
        TitleMarkIcon.Source = LoadLargestIconFrame();
        VersionLabel.Text = FormatAppVersion();
        Deactivated += MainWindow_OnDeactivated;
        IsVisibleChanged += MainWindow_OnIsVisibleChanged;
    }

    /// <summary>
    /// WPF <see cref="BitmapImage"/> on multi-frame .ico often picks a tiny layer; use the largest PNG frame for crisp UI scaling.
    /// </summary>
    private static ImageSource LoadLargestIconFrame()
    {
        var uri = new Uri("pack://application:,,,/SmoothMice.ico", UriKind.Absolute);
        var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames.Cast<BitmapFrame>().OrderByDescending(f => f.PixelWidth * f.PixelHeight).First();
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
        StopLiveApplyTimer();
        ((App)Application.Current).PersistFromUi();
        Hide();
    }

    private void MainWindow_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            StartLiveApplyTimer();
        else
            StopLiveApplyTimer();
    }

    private void StartLiveApplyTimer()
    {
        if (_liveApplyTimer is null)
        {
            _liveApplyTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _liveApplyTimer.Tick += LiveApplyTimer_OnTick;
        }
        _liveApplyTimer.Start();
    }

    private void StopLiveApplyTimer()
    {
        _liveApplyTimer?.Stop();
    }

    private void LiveApplyTimer_OnTick(object? sender, EventArgs e)
    {
        if (!_loaded) return;
        CommitAllNumericBindings();
        ((App)Application.Current).PersistFromUi();
    }

    private void CommitAllNumericBindings()
    {
        foreach (var tb in FindVisualChildren<TextBox>(this))
            BindingOperations.GetBindingExpression(tb, TextBox.TextProperty)?.UpdateSource();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t)
                yield return t;
            foreach (var c in FindVisualChildren<T>(child))
                yield return c;
        }
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

        if (_vmSubscribed != vm)
        {
            if (_vmSubscribed is not null)
                _vmSubscribed.PropertyChanged -= Vm_OnPropertyChanged;
            vm.PropertyChanged += Vm_OnPropertyChanged;
            _vmSubscribed = vm;
        }

        if (!_namesEventsWired)
        {
            vm.ProfileNames.CollectionChanged += (_, _) =>
            {
                _profileSuppress = true;
                ProfileCombo.SelectedItem = vm.SelectedProfile?.DisplayName;
                _profileSuppress = false;
                RequestSnapToContentAfterLayout();
            };
            _namesEventsWired = true;
        }

        _profileSuppress = true;
        ProfileCombo.ItemsSource  = vm.ProfileNames;
        ProfileCombo.SelectedItem = vm.SelectedProfile?.DisplayName;
        _profileSuppress = false;

        SyncPresetCombo(vm);
        _loaded = true;
        RequestSnapToContentAfterLayout();
    }

    private void Vm_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (
                nameof(MainViewModel.IsGlobalProfile)
                or nameof(MainViewModel.SelectedProfile)))
            return;

        RequestSnapToContentAfterLayout();
    }

    /// <summary>
    /// After first render we snap to device pixels (WPF chrome quirk). When visibility toggles
    /// (e.g. global-only checkbox), re-run size-to-content so nothing is clipped.
    /// </summary>
    private void RequestSnapToContentAfterLayout()
    {
        if (!_loaded) return;

        _snapRetryRemaining = 40;
        SizeToContent = SizeToContent.WidthAndHeight;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(SnapClientSizeToDevicePixels));
    }

    /// <summary>
    /// WPF: SizeToContent width+height with non-resizable chrome can leave a black strip at the client edge
    /// (HWND vs renderer misalignment). Snap outer size to whole device pixels and stop auto-sizing.
    /// https://github.com/dotnet/wpf/issues/9816
    /// </summary>
    private void MainWindow_OnContentRendered(object? sender, EventArgs e)
    {
        SnapClientSizeToDevicePixels();
    }

    /// <summary>
    /// After /tray startup the window can be minimized then hidden before real content layout;
    /// snapping then locks title-bar-only sizes. Re-run when visible and normal.
    /// </summary>
    public void RecalculateWindowSize() => RequestSnapToContentAfterLayout();

    private void SnapClientSizeToDevicePixels()
    {
        // /tray: minimized then hidden — never lock sizes here; OpenRequested calls RecalculateWindowSize.
        if (WindowState == WindowState.Minimized || Visibility != Visibility.Visible)
            return;

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            if (_snapRetryRemaining-- > 0)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(SnapClientSizeToDevicePixels));
            }

            return;
        }

        // Do not freeze collapsed measurements (minimized chrome, hidden window, transient layout).
        if (ActualWidth < 200 || ActualHeight < 120)
        {
            if (_snapRetryRemaining-- > 0)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(SnapClientSizeToDevicePixels));
            }

            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var physW = ActualWidth * dpi.DpiScaleX;
        var physH = ActualHeight * dpi.DpiScaleY;
        var snappedW = Math.Ceiling(physW) / dpi.DpiScaleX;
        var snappedH = Math.Ceiling(physH) / dpi.DpiScaleY;

        SizeToContent = SizeToContent.Manual;
        Width = Math.Max(snappedW, MinWidth);
        Height = Math.Max(snappedH, MinHeight);
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

        RequestSnapToContentAfterLayout();
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

    /// <summary>
    /// LostFocus can run before WPF flushes TwoWay bindings with UpdateSourceTrigger=LostFocus;
    /// force source update so Persist sees new values. Same path for Enter in <see cref="Numeric_OnKeyDown"/>.
    /// </summary>
    private void CommitNumericBinding(TextBox? tb)
    {
        if (!_loaded) return;
        if (tb is not null)
            BindingOperations.GetBindingExpression(tb, TextBox.TextProperty)?.UpdateSource();
        ((App)Application.Current).PersistFromUi();
    }

    private void Numeric_OnLostFocus(object sender, RoutedEventArgs e) =>
        CommitNumericBinding(sender as TextBox);

    private void Numeric_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        CommitNumericBinding(sender as TextBox);
        // Leave the TextBox so Enter reads as "done" (caret/border no longer on the field).
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            static () => Keyboard.ClearFocus());
    }

    private void Help_OnClick(object sender, RoutedEventArgs e)
    {
        var v = FormatAppVersion();
        MessageBox.Show(
            "SmoothMice makes mouse wheel scrolling on Windows smoother and more pleasant to use.\n\n" +
            $"Version {v}.\n\n" +
            "Created by luingry.\n" +
            "https://github.com/luingry/smoothmice",
            "About SmoothMice",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}