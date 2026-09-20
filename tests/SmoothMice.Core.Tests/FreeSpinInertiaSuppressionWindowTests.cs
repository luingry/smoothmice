using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using SmoothMice.App;
using SmoothMice.Core.Diagnostics;
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
                var monitor = new ScrollPulseMonitorWindow(hook);
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
}
