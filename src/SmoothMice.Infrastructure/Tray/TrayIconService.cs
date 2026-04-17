using System.Windows.Forms;
using SmoothMice.Infrastructure.Windows;

namespace SmoothMice.Infrastructure.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIconService()
    {
        _icon = new NotifyIcon
        {
            Visible = true,
            Text = "SmoothMice",
            Icon = IconFactory.CreateMouseIcon(32),
            ContextMenuStrip = new ContextMenuStrip(),
        };

        _ = _icon.ContextMenuStrip.Items.Add("Open settings", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        _ = _icon.ContextMenuStrip.Items.Add("Enable", null, (_, _) => ToggleEnableRequested?.Invoke(this, EventArgs.Empty));
        _ = _icon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _ = _icon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? ToggleEnableRequested;
    public event EventHandler? ExitRequested;

    public void SetBalloonTip(string title, string text) =>
        _icon.ShowBalloonTip(2500, title, text, ToolTipIcon.Info);

    public void SetEnabledMenuText(bool enabled)
    {
        if (_icon.ContextMenuStrip?.Items.Count > 1 && _icon.ContextMenuStrip.Items[1] is ToolStripMenuItem mi)
            mi.Text = enabled ? "Disable" : "Enable";
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
