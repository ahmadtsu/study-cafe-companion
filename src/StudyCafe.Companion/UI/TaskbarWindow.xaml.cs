// Adapted from EverythingToolbar (MIT © 2023 Stephan Rumswinkel). See ATTRIBUTION.md.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
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

        _animator = new TaskbarWindowAnimator(_handle, () => false);

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
            UpdatePosition(false);
        }
    }

    private void AttachToTaskbar()
    {
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

        try
        {
            int style = GetWindowLong(_handle, GWL_STYLE);
            style = (style & ~WS_POPUP) | WS_CHILD;
            SetWindowLong(_handle, GWL_STYLE, style);

            int exStyle = GetWindowLong(_handle, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            SetWindowLong(_handle, GWL_EXSTYLE, exStyle);

            NativeTaskbar.SetParent(_handle, _taskbarHandle);
            _docked = true;
            App.Log($"AttachToTaskbar: docked to 0x{_taskbarHandle:X}");

            HookTaskbarEvents();
            Show();
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
        if (_attachRetriesLeft-- <= 0) return;

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

    public void UpdatePosition(bool refreshLayoutElements = true)
    {
        if (!_docked || _taskbarHandle == nint.Zero)
        {
            App.Log($"UpdatePosition skipped: _docked={_docked}, handle=0x{_taskbarHandle:X}");
            return;
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
            var layout = _probe.Measure(handle, refreshLayoutElements);
            Dispatcher.BeginInvoke(() => ApplyLayout(handle, taskbarRect, layout, dpi, generation));
        });
    }

    private void ApplyLayout(nint handle, Rect taskbarRect, TaskbarLayout layout, double dpi, int generation)
    {
        if (generation != _positionGeneration || _animator is null) return;

        if (DateTime.UtcNow < _settleDeadline)
        {
            _refreshLayoutElements = false;
            _repositionTimer.Stop();
            _repositionTimer.Start();
        }

        // Measure widget content to ensure exact width
        var contentDip = WidgetView.ComputeDesiredContentWidth();
        var contentPx = contentDip * dpi;

        var bounds = TaskbarBounds.Calculate(handle, taskbarRect, layout, dpi, contentPx);
        App.Log($"ApplyLayout: taskbar={taskbarRect.Width}x{taskbarRect.Height}, obstacles={layout.Obstacles.Count}, cluster={layout.IconCluster?.Right}, contentDip={contentDip}, bounds={(bounds.HasValue ? $"{bounds.Value.Left},{bounds.Value.Top} {bounds.Value.Width}x{bounds.Value.Height}" : "null")}");

        if (bounds is { } b)
        {
            Width = b.Width / dpi;
            Height = b.Height / dpi;
            _animator.MoveTo(b);
        }
        else
        {
            _animator.Hide();
        }
    }

    private void HookTaskbarEvents()
    {
        if (_taskbarHandle == nint.Zero || _winEventCallback is not null) return;

        _winEventCallback = (_, _, hwnd, _, _, _, _) =>
        {
            if (hwnd == _taskbarHandle)
            {
                _settleDeadline = DateTime.UtcNow.AddMilliseconds(SettleMs);
                _repositionTimer.Stop();
                _repositionTimer.Start();
            }
        };

        _winEventHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE,
            EVENT_OBJECT_LOCATIONCHANGE,
            nint.Zero,
            _winEventCallback,
            0,
            0,
            WINEVENT_OUTOFCONTEXT);
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

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_taskbarCreatedMsg != 0 && (uint)msg == _taskbarCreatedMsg)
        {
            Debug.WriteLine("TaskbarCreated received, recovering dock state...");
            _docked = false;
            _probe.Invalidate();
            AttachToTaskbar();
            handled = true;
        }
        return nint.Zero;
    }

    private void Cleanup()
    {
        _repositionTimer.Stop();
        _dockRetryTimer?.Stop();
        UnhookTaskbarEvents();
        _animator?.StopAnimation();
    }
}
