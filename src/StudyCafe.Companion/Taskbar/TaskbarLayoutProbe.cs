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
/// Measures the Windows 11 taskbar through UI Automation.
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

    public TaskbarLayout Measure(nint taskbarHandle, bool refreshElements)
    {
        var obstacles = new List<Rect>();
        Rect? iconCluster = null;

        if (taskbarHandle != _cachedHandle || refreshElements)
        {
            Invalidate();
        }

        try
        {
            var taskbar = AutomationElement.FromHandle(taskbarHandle);
            if (taskbar is null)
            {
                return new TaskbarLayout(null, obstacles);
            }

            foreach (var icon in GetTrayIcons(taskbarHandle, taskbar, refreshElements))
            {
                var iconRect = icon.Current.BoundingRectangle;
                if (iconRect.Width > 0 && iconRect.Height > 0)
                {
                    obstacles.Add(iconRect);
                }
            }

            var frame = GetFrame(taskbarHandle, taskbar, refreshElements);
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
                    var rect = child.Current.BoundingRectangle;
                    if (rect.Width <= 0 || rect.Height <= 0)
                    {
                        continue;
                    }

                    if (child.Current.AutomationId is SystemTrayIconAutomationId or WidgetsButtonAutomationId)
                    {
                        obstacles.Add(rect);
                    }
                    else if ((isVertical ? rect.Height : rect.Width) <= maxIconDimension)
                    {
                        iconCluster = iconCluster.HasValue ? Rect.Union(iconCluster.Value, rect) : rect;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _cachedFrame = null;
            _cachedTrayIcons = null;
            Debug.WriteLine($"TaskbarLayoutProbe: error measuring taskbar: {ex.Message}");
        }

        return new TaskbarLayout(iconCluster, obstacles);
    }

    private AutomationElement? GetFrame(nint taskbarHandle, AutomationElement taskbar, bool refresh)
    {
        if (refresh || _cachedFrame == null || _cachedHandle != taskbarHandle)
        {
            _cachedFrame = taskbar.FindFirst(TreeScope.Descendants, ById(TaskbarFrameAutomationId));
            _cachedHandle = taskbarHandle;
        }

        return _cachedFrame;
    }

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
