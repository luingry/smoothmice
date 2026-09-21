using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmoothMice.App;
using SmoothMice.App.ViewModels;
using SmoothMice.Core.Config;
using SmoothMice.Core.Diagnostics;
using SmoothMice.Core.Profiles;
using SmoothMice.Infrastructure.Windows;
using Xunit;

namespace SmoothMice.Core.Tests;

public class FreeSpinInertiaSuppressionWindowTests
{
    [Fact]
    public void Window_constructs_on_sta_and_theme_updates_existing_semantic_brushes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new SmoothMice.App.App();
                app.InitializeComponent();
                // Production code sets this in OnStartup (not run here, since the test never
                // calls Application.Run()). Without it, the default OnLastWindowClose mode
                // shuts the Application down as soon as a dispatcher pump notices no window is
                // open, which breaks every window construction after the first Close().
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var themeProbe = new Border();
                themeProbe.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
                var themeWindow = new System.Windows.Window { Content = themeProbe };
                themeWindow.Show();
                Assert.Equal((Color)ColorConverter.ConvertFromString("#F4F5F7")!, ((SolidColorBrush)themeProbe.Background).Color);
                app.ApplyTheme(true);
                Assert.Equal((Color)ColorConverter.ConvertFromString("#1C1F24")!, ((SolidColorBrush)app.Resources["Brush.Surface"]).Color);
                Assert.Equal((Color)ColorConverter.ConvertFromString("#F1F5F9")!, ((SolidColorBrush)app.Resources["Brush.Text"]).Color);
                Assert.Equal((Color)ColorConverter.ConvertFromString("#1C1F24")!, ((SolidColorBrush)themeProbe.Background).Color);
                app.ApplyTheme(false);
                Assert.Equal((Color)ColorConverter.ConvertFromString("#F4F5F7")!, ((SolidColorBrush)app.Resources["Brush.Surface"]).Color);
                Assert.Equal((Color)ColorConverter.ConvertFromString("#1B1F26")!, ((SolidColorBrush)app.Resources["Brush.Text"]).Color);
                Assert.Equal((Color)ColorConverter.ConvertFromString("#F4F5F7")!, ((SolidColorBrush)themeProbe.Background).Color);
                themeWindow.Close();
                using var hook = new MouseHookService();
                using var recorder = new FreeSpinCalibrationRecorder(hook);
                var source = new ProfileAddSourceDialog();
                source.Close();
                var picker = new RunningWindowPickerDialog([]);
                picker.Close();
                var profiles = new ProfileManager(DefaultSettings.CreateAppSettings());
                using var coordinator = new ScrollCoordinator(profiles, hook, new ScrollInjector(), new ActiveAppResolver());
                var monitor = new ScrollPulseMonitorWindow(hook, profiles, coordinator);
                var monitorViewModel = (ScrollPulseMonitorViewModel)monitor.DataContext;
                var now = DateTimeOffset.UtcNow;
                monitorViewModel.Add(new ScrollPulseDiagnosticPulse(now, 1, false, 120, false, 0, 0), 40.0);
                monitorViewModel.Add(new ScrollPulseDiagnosticPulse(now.AddMilliseconds(50), 2, false, -120, false, 0, 0), 40.0);
                // Window.Show() ties layout to the native HWND lifecycle (SourceInitialized),
                // which does not complete in this headless test host (no interactive window
                // station). Measuring/arranging the content directly is a plain FrameworkElement
                // operation and does not depend on an HWND, so it still exercises real layout.
                var content = (FrameworkElement)monitor.Content!;
                content.Measure(new Size(748, 520));
                content.Arrange(new Rect(0, 0, 748, 520));
                content.UpdateLayout();
                var pulseList = (ListView)monitor.FindName("PulseList")!;
                pulseList.UpdateLayout();
                var container = pulseList.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem;
                Assert.True(container is not null,
                    $"No container generated (Items.Count={pulseList.Items.Count}, " +
                    $"GeneratorStatus={pulseList.ItemContainerGenerator.Status}).");
                container!.UpdateLayout();
                // Regression guard for the GridView-rows-render-as-one-string bug: the global
                // ListViewItem template is ContentPresenter-only, which silently ignores
                // GridViewColumns. A correctly templated row must expose a GridViewRowPresenter
                // with one realized cell per GridViewColumn.
                var rowPresenter = FindVisualChild<GridViewRowPresenter>(container);
                Assert.NotNull(rowPresenter);
                Assert.True(VisualTreeHelper.GetChildrenCount(rowPresenter) > 1,
                    "GridViewRowPresenter should have produced more than one cell.");
                monitor.Close();
                var window = new FreeSpinInertiaSuppressionWindow(
                    moduleEnabled: true,
                    liftTarget: 40,
                    landingTarget: 40,
                    repositionTarget: 40,
                    legitimateTarget: 40,
                    recorder,
                    _ => { },
                    (_, _) => { });
                window.Close();
                app.Shutdown();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                return typed;
            if (FindVisualChild<T>(child) is T found)
                return found;
        }
        return null;
    }
}
