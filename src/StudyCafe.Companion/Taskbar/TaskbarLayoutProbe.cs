// Adapted from EverythingToolbar (MIT © 2023 Stephan Rumswinkel). See ATTRIBUTION.md.
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using AutomationCondition = System.Windows.Automation.Condition;

namespace StudyCafe.Companion.Taskbar;

/// <summary>
/// The bounds of the taskbar's icon cluster (Start button plus task buttons) and the
/// elements a window placed on the taskbar must not overlap, all in screen pixels.
/// </summary>
internal readonly record struct TaskbarLayout(Rect? IconCluster, IReadOnlyList<Rect> Obstacles)
{
    public bool IsMeasurable => IconCluster.HasValue || Obstacles.Count > 0;
}

/// <summary>
/// Measures the Windows 11 taskbar through UI Automation and Win32 child bounds.
/// Walks the automation tree off the UI thread (via Task.Run).
/// </summary>
internal sealed class TaskbarLayoutProbe
{
    private const double MaxIconClusterChildWidthRatio = 0.6;
    private const string TaskbarFrameAutomationId = "TaskbarFrame";
    private const string SystemTrayIconAutomationId = "SystemTrayIcon";
    private const string WidgetsButtonAutomationId = "WidgetsButton";

    private nint _cachedHandle;
    private AutomationElement? _cachedFrame;
    private AutomationElement[]? _cachedTrayIcons;

    public void Invalidate()
    {
        _cachedFrame = null;
        _cachedTrayIcons = null;
        _cachedHandle = nint.Zero;
    }

    /// <param name="refreshElements">
    /// False re-reads the positions of the elements found last time instead of searching for them
    /// again. Valid while a reflow is settling, where things move but none come or go.
    /// </param>
    public TaskbarLayout Measure(nint taskbarHandle, bool refreshElements)
    {
        var obstacles = new List<Rect>();
        Rect? iconCluster = null;

        if (taskbarHandle != _cachedHandle)
        {
            Invalidate();
        }

        // Prefer the native TrayNotifyWnd container rect when available on Shell_TrayWnd:
        // Windows 11 keeps TrayNotifyWnd synchronized with the exact system tray layout bounds
        // (e.g. resizing immediately when the Microphone or another tray icon appears/disappears),
        // without suffering from the transient ~15px UIA BoundingRectangle expansion that occurs
        // when the "Show Hidden Icons" chevron rotates 180 degrees on open/close.
        bool hasNativeTrayRect = NativeTaskbar.TryGetTrayNotifyRect(taskbarHandle, out var trayNotifyRect);
        if (hasNativeTrayRect)
        {
            obstacles.Add(trayNotifyRect);
        }

        try
        {
            var taskbar = AutomationElement.FromHandle(taskbarHandle);
            if (taskbar is null)
            {
                return new TaskbarLayout(null, obstacles);
            }

            if (!hasNativeTrayRect)
            {
                var trayRects = new List<Rect>();
                foreach (var icon in GetTrayIcons(taskbarHandle, taskbar, refreshElements))
                {
                    try
                    {
                        var iconRect = icon.Current.BoundingRectangle;
                        if (iconRect.Width > 0 && iconRect.Height > 0)
                        {
                            trayRects.Add(iconRect);
                        }
                    }
                    catch
                    {
                        // Individual tray icon went stale during reflow; skip it.
                    }
                }

                obstacles.AddRange(NormalizeTrayIconRects(trayRects));
            }

            var frame = GetFrame(taskbarHandle, taskbar);
            if (frame != null)
            {
                var taskbarRect = taskbar.Current.BoundingRectangle;
                bool isVertical = taskbarRect.Height > taskbarRect.Width;
                double maxIconDimension =
                    (isVertical ? taskbarRect.Height : taskbarRect.Width) * MaxIconClusterChildWidthRatio;

                foreach (
                    AutomationElement child in frame.FindAll(TreeScope.Children, AutomationCondition.TrueCondition)
                )
                {
                    try
                    {
                        var rect = child.Current.BoundingRectangle;
                        if (rect.Width <= 0 || rect.Height <= 0)
                        {
                            continue;
                        }

                        var automationId = child.Current.AutomationId;
                        if (automationId == WidgetsButtonAutomationId ||
                            (!hasNativeTrayRect && automationId == SystemTrayIconAutomationId))
                        {
                            obstacles.Add(rect);
                        }
                        else if (automationId != SystemTrayIconAutomationId &&
                                 (isVertical ? rect.Height : rect.Width) <= maxIconDimension)
                        {
                            iconCluster = iconCluster.HasValue ? Rect.Union(iconCluster.Value, rect) : rect;
                        }
                    }
                    catch
                    {
                        // Element disappeared mid-enumeration; skip and continue.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Most likely a cached element that went away with its taskbar; resolve them again next time.
            _cachedFrame = null;
            _cachedTrayIcons = null;
            Debug.WriteLine($"TaskbarLayoutProbe: error measuring taskbar: {ex.Message}");
        }

        return new TaskbarLayout(iconCluster, obstacles);
    }

    /// <summary>
    /// When the "Show Hidden Icons" chevron rotates 180 degrees on click, its axis-aligned UIA
    /// BoundingRectangle expands symmetrically around its center, causing its Right edge to overlap
    /// the adjacent tray icon and its Left edge to protrude by the same amount. Trim any symmetric
    /// overlap on the leftmost icon so fallback UIA measurements never bounce.
    /// </summary>
    internal static IReadOnlyList<Rect> NormalizeTrayIconRects(List<Rect> rects)
    {
        if (rects.Count < 2)
        {
            return rects;
        }

        rects.Sort((a, b) => a.Left.CompareTo(b.Left));
        var first = rects[0];
        var second = rects[1];
        double overlap = first.Right - second.Left;
        if (overlap > 0.5 && first.Width > overlap * 2)
        {
            rects[0] = new Rect(
                first.Left + overlap,
                first.Top,
                first.Width - (overlap * 2),
                first.Height);
        }

        return rects;
    }

    /// <summary>
    /// Finding the frame is a full descendant walk, and it outlives every reflow, so it is resolved
    /// once per taskbar rather than on each measurement.
    /// </summary>
    private AutomationElement? GetFrame(nint taskbarHandle, AutomationElement taskbar)
    {
        if (_cachedFrame == null || _cachedHandle != taskbarHandle)
        {
            _cachedFrame = taskbar.FindFirst(TreeScope.Descendants, ById(TaskbarFrameAutomationId));
            _cachedHandle = taskbarHandle;
        }

        return _cachedFrame;
    }

    /// <summary>
    /// Depending on the Windows build the tray icons sit outside the taskbar frame, so they are
    /// searched from the root rather than among the frame's children.
    /// </summary>
    private AutomationElement[] GetTrayIcons(nint taskbarHandle, AutomationElement taskbar, bool refresh)
    {
        if (refresh || _cachedTrayIcons == null || _cachedHandle != taskbarHandle)
        {
            _cachedTrayIcons = taskbar
                .FindAll(TreeScope.Descendants, ById(SystemTrayIconAutomationId))
                .Cast<AutomationElement>()
                .ToArray();
            _cachedHandle = taskbarHandle;
        }

        return _cachedTrayIcons;
    }

    private static PropertyCondition ById(string automationId) =>
        new(AutomationElement.AutomationIdProperty, automationId);
}
