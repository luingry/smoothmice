using System.Threading;
using System.Windows.Media;
using SmoothMice.App;
using SmoothMice.Core.Diagnostics;
using SmoothMice.Infrastructure.Windows;
using Xunit;

namespace SmoothMice.Core.Tests;

public class FreeSpinInertiaSuppressionWindowTests
{
    [Fact]
    public void Window_constructs_on_sta_with_read_only_phase_bindings()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new SmoothMice.App.App();
                app.Resources["Brush.Surface"] = Brush("#F4F5F7");
                app.Resources["Brush.Card"] = Brush("#FFFFFF");
                app.Resources["Brush.Border"] = Brush("#E4E6EB");
                app.Resources["Brush.Text"] = Brush("#1B1F26");
                app.Resources["Brush.Muted"] = Brush("#6B7280");
                app.Resources["Brush.Accent"] = Brush("#2563EB");
                using var hook = new MouseHookService();
                using var recorder = new FreeSpinCalibrationRecorder(hook);
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

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex)!);
}
