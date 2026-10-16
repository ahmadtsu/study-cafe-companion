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
            Icon = GetOrCreateIcon(DrawingColor.FromArgb(255, 224, 109, 83)),
        };

        _notifyIcon.DoubleClick += (_, _) => OpenInBrowser();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    private readonly Dictionary<int, (DrawingIcon Icon, nint HIcon)> _iconCache = new();
    private DrawingColor? _currentIconColor;

    public void UpdateStatus(string formattedTime, string phase, int participants, bool isPaused)
    {
        try
        {
            string phaseLabel = phase switch
            {
                "work" => isPaused ? "Focus (Paused)" : "Focus",
                "break" => isPaused ? "Break (Paused)" : "Break",
                "break_ended" => isPaused ? "Break Over (Paused)" : "Break Over",
                "left_table" => "Left Table",
                _ => "Break Over",
            };

            DrawingColor iconColor = phase switch
            {
                "work" => DrawingColor.FromArgb(255, 224, 109, 83),
                "break" => DrawingColor.FromArgb(255, 94, 170, 141),
                _ => DrawingColor.FromArgb(255, 239, 68, 68), // Red timer on break over, left table, and non-focus
            };

            _statusItem.Text = $"Study Cafe • {phaseLabel} ({formattedTime})";
            var text = $"Study Cafe: {phaseLabel} ({formattedTime})";
            _notifyIcon.Text = text.Length > 63
                ? $"Study Cafe: {formattedTime}"
                : text;

            if (_currentIconColor != iconColor)
            {
                _currentIconColor = iconColor;
                _notifyIcon.Icon = GetOrCreateIcon(iconColor);
            }
        }
        catch (Exception ex)
        {
            App.Log($"TrayIconManager.UpdateStatus error: {ex.Message}");
        }
    }

    private DrawingIcon GetOrCreateIcon(DrawingColor strokeColor)
    {
        int key = strokeColor.ToArgb();
        if (_iconCache.TryGetValue(key, out var cached))
        {
            return cached.Icon;
        }

        using var bmp = new DrawingBitmap(32, 32);
        using var g = DrawingGraphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var pen = new DrawingPen(strokeColor, 2.5f);
        g.DrawEllipse(pen, 2, 2, 28, 28);
        g.DrawEllipse(pen, 8, 8, 16, 16);
        g.DrawEllipse(pen, 13, 13, 6, 6);

        var hIcon = bmp.GetHicon();
        var icon = DrawingIcon.FromHandle(hIcon);
        _iconCache[key] = (icon, hIcon);
        return icon;
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

        foreach (var pair in _iconCache.Values)
        {
            try { pair.Icon.Dispose(); } catch { }
            try { DestroyIcon(pair.HIcon); } catch { }
        }
        _iconCache.Clear();
    }
}
