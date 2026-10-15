// Adapted from EverythingToolbar (MIT © 2023 Stephan Rumswinkel). See ATTRIBUTION.md.
using System.Diagnostics;
using System.Windows;

namespace StudyCafe.Companion.Taskbar;

internal readonly record struct WidgetBounds(int Left, int Top, int Width, int Height)
{
    public static WidgetBounds Lerp(WidgetBounds from, WidgetBounds to, double progress) =>
        new(
            Interpolate(from.Left, to.Left, progress),
            Interpolate(from.Top, to.Top, progress),
            Interpolate(from.Width, to.Width, progress),
            Interpolate(from.Height, to.Height, progress)
        );

    private static int Interpolate(int from, int to, double progress) =>
        (int)Math.Round(from + (to - from) * progress);
}

/// <summary>
/// Geometry calculator that places the Study Cafe widget on the right of the taskbar,
/// immediately to the left of the system tray chevron / notification area.
/// </summary>
internal static class TaskbarBounds
{
    private const double MinWidgetWidthDip = 36;
    private const double DefaultWidgetHeightDip = 36;
    private const double HorizontalMarginDip = 2;
    private const double VerticalMarginDip = 6;

    public static WidgetBounds? Calculate(
        nint taskbarHandle,
        Rect taskbarRectPx,
        TaskbarLayout layout,
        double dpiScale,
        double desiredContentWidthPx)
    {
        if (dpiScale <= 0) return null;

        int taskbarWidth = (int)Math.Round(taskbarRectPx.Width);
        int taskbarHeight = (int)Math.Round(taskbarRectPx.Height);
        if (taskbarWidth <= 0 || taskbarHeight <= 0) return null;

        // Vertical taskbar: ignore or fallback
        if (taskbarHeight > taskbarWidth) return null;

        Rect? cluster = null;
        if (layout.IconCluster is { } c &&
            c.Bottom > taskbarRectPx.Top && c.Top < taskbarRectPx.Bottom &&
            c.Right > taskbarRectPx.Left && c.Left < taskbarRectPx.Right)
        {
            cluster = c;
        }

        var relevantObstacles = layout.Obstacles
            .Where(o => o.Bottom > taskbarRectPx.Top && o.Top < taskbarRectPx.Bottom &&
                        o.Right > taskbarRectPx.Left && o.Left < taskbarRectPx.Right)
            .ToList();

        // Right alignment: target the system tray icons/chevron which live on the right half of the taskbar.
        // We filter out left-side obstacles (e.g. WidgetsButton at x=0) so they don't falsely collapse the gap.
        double midTaskbar = taskbarRectPx.Left + (taskbarRectPx.Width * 0.4);
        double trayLeft = relevantObstacles
            .Select(o => o.Left)
            .Where(x => x >= midTaskbar)
            .Append(taskbarRectPx.Right)
            .Min();

        double gapStart = cluster?.Right ?? taskbarRectPx.Left;
        double gapEnd = trayLeft;

        // If the cluster extends past the tray (e.g. narrow display), clamp gapStart
        if (gapStart > gapEnd - (MinWidgetWidthDip * dpiScale))
        {
            gapStart = taskbarRectPx.Left;
        }

        int margin = (int)Math.Round(HorizontalMarginDip * dpiScale);
        var (gapLeft, gapRight) = ProjectSpan(taskbarHandle, gapStart, gapEnd, margin);
        int available = gapRight - gapLeft;

        if (available < (int)(MinWidgetWidthDip * dpiScale))
        {
            return null;
        }

        int height = taskbarHeight;
        int targetWidth = (int)Math.Ceiling(desiredContentWidthPx);
        if (targetWidth <= 0)
        {
            targetWidth = (int)Math.Round(75 * dpiScale);
        }

        int width = Math.Min(available, targetWidth);

        // Place on the far right of the free stretch (directly left of the system tray chevron)
        int x = gapRight - width;
        int y = 0;

        return new WidgetBounds(x, y, width, height);
    }

    private static (int Left, int Right) ProjectSpan(nint taskbarHandle, double screenLeft, double screenRight, int padding)
    {
        var startPt = new NativeTaskbar.POINT { X = (int)Math.Round(screenLeft), Y = 0 };
        var endPt = new NativeTaskbar.POINT { X = (int)Math.Round(screenRight), Y = 0 };

        NativeTaskbar.ScreenToClient(taskbarHandle, ref startPt);
        NativeTaskbar.ScreenToClient(taskbarHandle, ref endPt);

        int spanStart = Math.Min(startPt.X, endPt.X) + padding;
        int spanEnd = Math.Max(startPt.X, endPt.X) - padding;

        return (spanStart, spanEnd);
    }
}
