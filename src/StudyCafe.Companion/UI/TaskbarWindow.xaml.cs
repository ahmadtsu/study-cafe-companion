// Adapted from EverythingToolbar (MIT © 2023 Stephan Rumswinkel). See ATTRIBUTION.md.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using StudyCafe.Companion.Taskbar;
using StudyCafe.Companion.Themes;

namespace StudyCafe.Companion.UI;

public partial class TaskbarWindow : Window
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int WM_DPICHANGED = 0x02E0;

    private const int AttachRetryCount = 5;
    private const int RepositionMs = 150;
    private const int SettleMs = 1200;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc,
        WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hWinEventHook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    private delegate void WinEventProc(
        nint hWinEventHook, uint eventType, nint hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

    private readonly TaskbarLayoutProbe _probe = new();
    private TaskbarWindowAnimator? _animator;
    private nint _handle;
    private nint _taskbarHandle;
    private bool _docked;
    private int _attachRetriesLeft = AttachRetryCount;
    private DispatcherTimer? _dockRetryTimer;
    private int _positionGeneration;
    private bool _refreshLayoutElements = true;
    private DateTime _settleDeadline;
    private uint _taskbarCreatedMsg;

    private Rect _lastTaskbarRect;
    private TaskbarLayout? _lastLayout;
    private double _lastDpi = 1.0;

    private readonly DispatcherTimer _repositionTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(RepositionMs),
    };

    private WinEventProc? _winEventCallback;
    private nint _winEventHook;

    public TaskbarWindow(nint taskbarHandle = 0)
    {
        _taskbarHandle = taskbarHandle;
        InitializeComponent();

        _repositionTimer.Tick += (_, _) =>
        {
            _repositionTimer.Stop();
            var refresh = _refreshLayoutElements;
            _refreshLayoutElements = true;
            UpdatePosition(refresh);
        };

        Loaded += (_, _) => UpdatePosition();
        Closed += (_, _) => Cleanup();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void Initialize()
    {
        var helper = new WindowInteropHelper(this);
        helper.EnsureHandle();
        _handle = helper.Handle;

        if (_handle == nint.Zero)
        {
            App.Log("TaskbarWindow.Initialize: handle is zero");
            return;
        }

        _animator = new TaskbarWindowAnimator(_handle, () => !SystemParameters.ClientAreaAnimation);

        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);

        AttachToTaskbar();
    }

    private string _lastPhase = "";
    private double _lastReportedWidth = 0;

    public void UpdateTimerState(string formattedTime, string phase, int participants, bool isPaused)
    {
        WidgetView.UpdateState(formattedTime, phase, participants, isPaused);

        double width = WidgetView.ComputeDesiredContentWidth();
        if (_lastPhase != phase || Math.Abs(width - _lastReportedWidth) > 3)
        {
            _lastPhase = phase;
            _lastReportedWidth = width;
            UpdatePosition(true);
        }
    }

    private bool CanDock() =>
        Environment.OSVersion.Version.Build >= 22000 && !NativeTaskbar.IsClassicTaskbar(_taskbarHandle);

    private void AttachToTaskbar()
    {
        if (_handle == nint.Zero)
        {
            return;
        }

        if (_taskbarHandle == nint.Zero || !NativeTaskbar.IsWindow(_taskbarHandle))
        {
            _taskbarHandle = NativeTaskbar.FindTaskbarHandle();
        }

        if (_taskbarHandle == nint.Zero || !NativeTaskbar.IsWindow(_taskbarHandle))
        {
            App.Log($"AttachToTaskbar: Taskbar handle 0x{_taskbarHandle:X} not found, scheduling retry");
            ScheduleDockRetry();
            return;
        }

        if (!CanDock())
        {
            App.Log($"AttachToTaskbar: not dockable (build={Environment.OSVersion.Version.Build}, classic={NativeTaskbar.IsClassicTaskbar(_taskbarHandle)}, tray=0x{_taskbarHandle:X})");
            ScheduleDockRetry();
            return;
        }

        try
        {
            int style = GetWindowLong(_handle, GWL_STYLE);
            style = (style & ~WS_POPUP) | WS_CHILD;
            SetWindowLong(_handle, GWL_STYLE, style);

            int exStyle = GetWindowLong(_handle, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            SetWindowLong(_handle, GWL_EXSTYLE, exStyle);

            if (NativeTaskbar.SetParent(_handle, _taskbarHandle) == nint.Zero &&
                NativeTaskbar.GetParent(_handle) != _taskbarHandle)
            {
                App.Log($"AttachToTaskbar: SetParent to 0x{_taskbarHandle:X} failed, scheduling retry");
                _animator?.Hide();
                ScheduleDockRetry();
                return;
            }

            _docked = true;
            _attachRetriesLeft = AttachRetryCount;
            App.Log($"AttachToTaskbar: docked to 0x{_taskbarHandle:X}");

            HookTaskbarEvents();
            Show();

            // Ensure Show() did not strip WS_CHILD or parenting
            if (NativeTaskbar.GetParent(_handle) != _taskbarHandle)
            {
                int postShowStyle = GetWindowLong(_handle, GWL_STYLE);
                SetWindowLong(_handle, GWL_STYLE, (postShowStyle & ~WS_POPUP) | WS_CHILD);
                NativeTaskbar.SetParent(_handle, _taskbarHandle);
            }

            // Hide native HWND until ApplyLayout calculates valid bounds inside the taskbar
            _animator?.Hide();
            _settleDeadline = DateTime.UtcNow.AddMilliseconds(SettleMs);
            UpdatePosition();
        }
        catch (Exception ex)
        {
            App.Log($"AttachToTaskbar exception: {ex.Message}");
            ScheduleDockRetry();
        }
    }

    private void ScheduleDockRetry()
    {
        if (_dockRetryTimer?.IsEnabled == true) return;

        if (_attachRetriesLeft-- <= 0)
        {
            _attachRetriesLeft = 10;
        }

        if (_dockRetryTimer == null)
        {
            _dockRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _dockRetryTimer.Tick += (_, _) =>
            {
                _dockRetryTimer.Stop();
                AttachToTaskbar();
            };
        }
        _dockRetryTimer.Start();
    }

    public void HandleDisplaySettingsChanged()
    {
        if (_handle == nint.Zero)
        {
            return;
        }

        App.Log($"TaskbarWindow (0x{_handle:X}, taskbar=0x{_taskbarHandle:X}): HandleDisplaySettingsChanged");
        _probe.Invalidate();
        _lastLayout = null;

        if (_taskbarHandle == nint.Zero || !NativeTaskbar.IsWindow(_taskbarHandle))
        {
            _docked = false;
            _taskbarHandle = nint.Zero;
            AttachToTaskbar();
            return;
        }

        _lastDpi = NativeTaskbar.GetDpiForWindow(_taskbarHandle) / 96.0;
        _settleDeadline = DateTime.UtcNow.AddMilliseconds(SettleMs);
        _refreshLayoutElements = true;
        _repositionTimer.Stop();
        _repositionTimer.Start();
        UpdatePosition(refreshLayoutElements: true);
    }

    public void UpdatePosition(bool refreshLayoutElements = true)
    {
        if (_handle == nint.Zero || !_docked)
        {
            return;
        }

        if (_taskbarHandle == nint.Zero || !NativeTaskbar.IsWindow(_taskbarHandle))
        {
            _taskbarHandle = NativeTaskbar.FindTaskbarHandle();
        }

        if (_taskbarHandle == nint.Zero || !NativeTaskbar.IsWindow(_taskbarHandle))
        {
            App.Log("UpdatePosition: taskbar handle no longer valid, re-docking");
            _docked = false;
            ScheduleDockRetry();
            return;
        }

        if (NativeTaskbar.GetParent(_handle) != _taskbarHandle)
        {
            int style = GetWindowLong(_handle, GWL_STYLE);
            SetWindowLong(_handle, GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD);
            NativeTaskbar.SetParent(_handle, _taskbarHandle);

            if (NativeTaskbar.GetParent(_handle) != _taskbarHandle)
            {
                App.Log("UpdatePosition: window lost taskbar parent and SetParent failed, re-docking");
                _animator?.Hide();
                _docked = false;
                ScheduleDockRetry();
                return;
            }
        }

        if (!NativeTaskbar.TryGetVisibleTaskbarRect(_taskbarHandle, out var taskbarRect))
        {
            App.Log("UpdatePosition: TryGetVisibleTaskbarRect returned false");
            if (DateTime.UtcNow < _settleDeadline)
            {
                _repositionTimer.Start();
            }
            return;
        }

        var generation = ++_positionGeneration;
        var handle = _taskbarHandle;
        var dpi = NativeTaskbar.GetDpiForWindow(handle) / 96.0;

        Task.Run(() =>
        {
            NativeTaskbar.EnsureInteractiveDesktop();
            var layout = _probe.Measure(handle, refreshLayoutElements);
            Dispatcher.BeginInvoke(() => ApplyLayout(handle, taskbarRect, layout, dpi, generation));
        });
    }

    private void ApplyLayout(nint handle, Rect taskbarRect, TaskbarLayout layout, double dpi, int generation)
    {
        if (generation != _positionGeneration || _animator is null) return;

        // Re-check until the taskbar has finished moving; a new burst of events pushes the deadline.
        if (DateTime.UtcNow < _settleDeadline)
        {
            _refreshLayoutElements = false;
            _repositionTimer.Stop();
            _repositionTimer.Start();
        }

        if (!layout.IsMeasurable)
        {
            App.Log("ApplyLayout: probe nothing measurable yet, scheduling retry");
            _refreshLayoutElements = true;
            _repositionTimer.Stop();
            _repositionTimer.Start();
            return;
        }

        // Fallback to last known valid cluster or obstacles on the same taskbar during transient UIA states
        var effectiveCluster = layout.IconCluster;
        if (!effectiveCluster.HasValue && _lastLayout?.IconCluster is { } lastCluster &&
            _lastTaskbarRect == taskbarRect)
        {
            effectiveCluster = lastCluster;
        }

        var effectiveObstacles = layout.Obstacles;
        if (effectiveObstacles.Count == 0 && _lastLayout is { Obstacles.Count: > 0 } lastWithObstacles &&
            _lastTaskbarRect == taskbarRect)
        {
            effectiveObstacles = lastWithObstacles.Obstacles;
        }

        // Right-aligned widget requires system tray obstacles to anchor to the left of the tray chevron.
        // If tray obstacles haven't loaded yet, wait and retry rather than anchoring to taskbarRect.Right.
        if (effectiveObstacles.Count == 0)
        {
            App.Log("ApplyLayout: tray obstacles not measurable yet, scheduling retry");
            _refreshLayoutElements = true;
            _repositionTimer.Stop();
            _repositionTimer.Start();
            return;
        }

        var effectiveLayout = new TaskbarLayout(effectiveCluster, effectiveObstacles);

        _lastTaskbarRect = taskbarRect;
        if (effectiveCluster.HasValue && effectiveObstacles.Count > 0)
        {
            _lastLayout = effectiveLayout;
        }
        _lastDpi = dpi;

        // Verify the window is still a child of the taskbar before applying client coordinates
        if (NativeTaskbar.GetParent(_handle) != handle)
        {
            int style = GetWindowLong(_handle, GWL_STYLE);
            SetWindowLong(_handle, GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD);
            NativeTaskbar.SetParent(_handle, handle);

            if (NativeTaskbar.GetParent(_handle) != handle)
            {
                App.Log("ApplyLayout: window is not parented to taskbar; hiding and scheduling re-dock");
                _animator.Hide();
                _docked = false;
                ScheduleDockRetry();
                return;
            }
        }

        // Measure widget content to ensure exact width
        var contentDip = WidgetView.ComputeDesiredContentWidth();
        var contentPx = contentDip * dpi;

        var centerAligned = IsTaskbarCenterAligned();
        var bounds = TaskbarBounds.Calculate(handle, taskbarRect, effectiveLayout, dpi, contentPx, centerAligned);
        if (bounds != _animator.CurrentBounds)
        {
            App.Log($"ApplyLayout: taskbar={taskbarRect.Width}x{taskbarRect.Height}, obstacles={effectiveLayout.Obstacles.Count}, cluster={effectiveLayout.IconCluster?.Right}, contentDip={contentDip}, bounds={(bounds.HasValue ? $"{bounds.Value.Left},{bounds.Value.Top} {bounds.Value.Width}x{bounds.Value.Height}" : "null")}");
        }

        if (bounds is { } b)
        {
            Width = b.Width / dpi;
            Height = b.Height / dpi;
            _animator.MoveTo(b);
        }
        else
        {
            if (_animator.CurrentBounds != null && (DateTime.UtcNow < _settleDeadline || !layout.IconCluster.HasValue))
            {
                App.Log("ApplyLayout: no room calculated during transient state; preserving current placement");
            }
            else
            {
                _animator.Hide();
            }
        }
    }

    private static bool IsTaskbarCenterAligned()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return (int?)key?.GetValue("TaskbarAl", 1) != 0;
        }
        catch
        {
            return true;
        }
    }

    private void HookTaskbarEvents()
    {
        UnhookTaskbarEvents();
        if (_taskbarHandle == nint.Zero) return;

        uint threadId = NativeTaskbar.GetWindowThreadProcessId(_taskbarHandle, out uint processId);
        if (threadId == 0)
        {
            return;
        }

        // Adapted from EverythingToolbar: scope the hook to the taskbar's own processId and threadId
        // so we hear about all layout changes inside the taskbar (system tray icons appearing/moving,
        // TrayNotifyWnd resizing, task buttons shifting) rather than checking hwnd == _taskbarHandle.
        _winEventCallback = OnTaskbarEvent;
        _winEventHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE,
            EVENT_OBJECT_LOCATIONCHANGE,
            nint.Zero,
            _winEventCallback,
            processId,
            threadId,
            WINEVENT_OUTOFCONTEXT);
    }

    private void OnTaskbarEvent(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        // Avoid feedback loop if the location change came from our own docked HWND
        if (hwnd != nint.Zero && hwnd == _handle)
        {
            return;
        }

        // The taskbar emits location changes in bursts; coalesce them and reposition once they stop.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RestartRepositionTimer);
            return;
        }

        RestartRepositionTimer();
    }

    private void RestartRepositionTimer()
    {
        _settleDeadline = DateTime.UtcNow.AddMilliseconds(SettleMs);
        _repositionTimer.Stop();
        _repositionTimer.Start();
    }

    private void UnhookTaskbarEvents()
    {
        if (_winEventHook != nint.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = nint.Zero;
        }
        _winEventCallback = null;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            App.Log("SystemEvents.DisplaySettingsChanged received");
            HandleDisplaySettingsChanged();
        });
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;

        Dispatcher.BeginInvoke(() =>
        {
            App.Log("SystemEvents.PowerModeChanged (Resume) received");
            HandleDisplaySettingsChanged();
        });
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is not (SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)) return;

        Dispatcher.BeginInvoke(() =>
        {
            App.Log($"SystemEvents.SessionSwitch ({e.Reason}) received");
            NativeTaskbar.EnsureInteractiveDesktop();
            HandleDisplaySettingsChanged();
        });
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg is WM_DISPLAYCHANGE or WM_DPICHANGED)
        {
            HandleDisplaySettingsChanged();
            return nint.Zero;
        }

        if (_taskbarCreatedMsg != 0 && (uint)msg == _taskbarCreatedMsg)
        {
            App.Log("TaskbarCreated received, recovering dock state...");
            _docked = false;
            _taskbarHandle = nint.Zero;
            _attachRetriesLeft = AttachRetryCount;
            _probe.Invalidate();
            _lastLayout = null;
            AttachToTaskbar();
            handled = true;
        }
        return nint.Zero;
    }

    private void Cleanup()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _repositionTimer.Stop();
        _dockRetryTimer?.Stop();
        UnhookTaskbarEvents();
        _animator?.StopAnimation();
    }
}
