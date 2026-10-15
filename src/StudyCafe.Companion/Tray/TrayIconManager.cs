using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using StudyCafe.Companion.Core;
using DrawingColor = System.Drawing.Color;
using DrawingIcon = System.Drawing.Icon;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPen = System.Drawing.Pen;

namespace StudyCafe.Companion.Tray;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _autostartItem;
    private bool _disposed;

    public TrayIconManager()
    {
        _contextMenu = new ContextMenuStrip();

        _statusItem = new ToolStripMenuItem("Study Cafe • Ready") { Enabled = false };
        _contextMenu.Items.Add(_statusItem);
        _contextMenu.Items.Add(new ToolStripSeparator());

        var openItem = new ToolStripMenuItem("Open Study Cafe in Browser", null, (_, _) => OpenInBrowser());
        _contextMenu.Items.Add(openItem);

        _autostartItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutostart())
        {
            Checked = AutostartHelper.IsEnabled(),
        };
        _contextMenu.Items.Add(_autostartItem);
        _contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => Application.Current.Shutdown());
        _contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "Study Cafe Companion",
            ContextMenuStrip = _contextMenu,
            Visible = true,
            Icon = CreateCirclesIcon(DrawingColor.FromArgb(255, 224, 109, 83)),
        };

        _notifyIcon.DoubleClick += (_, _) => OpenInBrowser();
    }

    public void UpdateStatus(string formattedTime, string phase, int participants, bool isPaused)
    {
        string phaseLabel = phase switch
        {
            "work" => isPaused ? "Focus (Paused)" : "Focus",
            "break" or "break_ended" => isPaused ? "Break (Paused)" : "Break",
            _ => "Ready",
        };

        DrawingColor iconColor = phase switch
        {
            "work" => DrawingColor.FromArgb(255, 224, 109, 83),
            "break" or "break_ended" => DrawingColor.FromArgb(255, 94, 170, 141),
            _ => DrawingColor.FromArgb(255, 142, 142, 147),
        };

        _statusItem.Text = $"Study Cafe • {phaseLabel} ({formattedTime})";
        _notifyIcon.Text = $"Study Cafe: {phaseLabel} ({formattedTime})".Length > 63
            ? $"Study Cafe: {formattedTime}"
            : $"Study Cafe: {phaseLabel} ({formattedTime})";

        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = CreateCirclesIcon(iconColor);
        oldIcon?.Dispose();
    }

    private DrawingIcon CreateCirclesIcon(DrawingColor strokeColor)
    {
        using var bmp = new DrawingBitmap(32, 32);
        using var g = DrawingGraphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new DrawingPen(strokeColor, 2.5f);
        g.DrawEllipse(pen, 2, 2, 28, 28);
        g.DrawEllipse(pen, 8, 8, 16, 16);
        g.DrawEllipse(pen, 13, 13, 6, 6);

        var hIcon = bmp.GetHicon();
        return DrawingIcon.FromHandle(hIcon);
    }

    private static void OpenInBrowser()
    {
        try
        {
            var url = AppSettings.Current.WebUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = "https://thestudycafe.vercel.app";
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open browser: {ex.Message}");
        }
    }

    private void ToggleAutostart()
    {
        bool newState = !_autostartItem.Checked;
        _autostartItem.Checked = newState;
        AutostartHelper.SetEnabled(newState);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }
}
