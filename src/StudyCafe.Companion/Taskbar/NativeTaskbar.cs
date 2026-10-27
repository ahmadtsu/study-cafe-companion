// Adapted from EverythingToolbar (MIT © 2023 Stephan Rumswinkel). See ATTRIBUTION.md.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace StudyCafe.Companion.Taskbar;

/// <summary>
/// Native P/Invoke taskbar helpers (user32/shell32).
/// All wrappers swallow native failures and return safe defaults instead of throwing.
/// </summary>
internal static class NativeTaskbar
{
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_HIDEWINDOW = 0x0080;

    private const string TaskbarXamlIslandClass = "Windows.UI.Composition.DesktopWindowContentBridge";
    private const int GW_CHILD = 5;
    private const int GW_HWNDNEXT = 2;
    private const int ABM_GETSTATE = 0x00000004;
    private const uint ABS_AUTOHIDE = 0x00000001;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool EnumChildProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowEx(nint hWndParent, nint hWndChildAfter, string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetWindow(nint hWnd, int uCmd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint hWndParent, EnumChildProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("shell32.dll")]
    private static extern uint SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint GetDpiForWindowPrivate(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static extern uint GetWindowThreadProcessIdPrivate(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "ScreenToClient", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClientPrivate(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRectPrivate(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "SetParent", SetLastError = true)]
    private static extern nint SetParentPrivate(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll", EntryPoint = "GetParent", SetLastError = true)]
    private static extern nint GetParentPrivate(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPosPrivate(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", EntryPoint = "OpenInputDesktop")]
    private static extern nint OpenInputDesktopPrivate(uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenDesktop")]
    private static extern nint OpenDesktopPrivate(string lpszDesktop, uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", EntryPoint = "SetThreadDesktop")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadDesktopPrivate(nint hDesktop);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        string? lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetThreadDesktop(int dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(nint hObj, int nIndex, StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);

    internal static bool EnsureInteractiveDesktop()
    {
        try
        {
            var hDesk = OpenInputDesktopPrivate(0, false, 0x01FF);
            if (hDesk == nint.Zero)
            {
                hDesk = OpenDesktopPrivate("Default", 0, false, 0x01FF);
            }

            if (hDesk != nint.Zero)
            {
                return SetThreadDesktopPrivate(hDesk);
            }
        }
        catch
        {
        }

        return false;
    }

    internal static string GetCurrentDesktopName()
    {
        try
        {
            nint hDesk = GetThreadDesktop(GetCurrentThreadId());
            if (hDesk == nint.Zero) return string.Empty;

            var sb = new StringBuilder(256);
            return GetUserObjectInformation(hDesk, 2, sb, sb.Capacity, out _) ? sb.ToString() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// If the process was started on an isolated or virtual desktop (e.g. from an IDE terminal or sandbox),
    /// re-launches the application onto WinSta0\Default so it can dock into Shell_TrayWnd on the user's screen.
    /// Returns false if re-launched (caller should exit).
    /// </summary>
    internal static bool EnsureRunningOnInteractiveDesktop(string[] args)
    {
        string currentDesk = GetCurrentDesktopName();
        if (string.IsNullOrEmpty(currentDesk) || string.Equals(currentDesk, "Default", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        App.Log($"Current desktop is '{currentDesk}' (not 'Default'). Re-launching on WinSta0\\Default...");

        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath))
            {
                exePath = Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
            {
                var argString = args.Length > 0 ? " " + string.Join(" ", args.Select(a => $"\"{a}\"")) : "";
                var cmdLine = $"\"{exePath}\"{argString}";

                var si = new STARTUPINFO();
                si.cb = Marshal.SizeOf<STARTUPINFO>();
                si.lpDesktop = @"WinSta0\Default";

                string? workingDir = System.IO.Path.GetDirectoryName(exePath);

                if (CreateProcess(null, cmdLine, nint.Zero, nint.Zero, false, 0, nint.Zero, workingDir, ref si, out var pi))
                {
                    App.Log($"Successfully re-launched on WinSta0\\Default (PID {pi.dwProcessId}). Exiting current process.");
                    if (pi.hProcess != nint.Zero) CloseHandle(pi.hProcess);
                    if (pi.hThread != nint.Zero) CloseHandle(pi.hThread);
                    return false;
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    App.Log($"Failed to re-launch on WinSta0\\Default: error {err}");
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"EnsureRunningOnInteractiveDesktop exception: {ex.Message}");
        }

        return true;
    }

    internal static nint FindTaskbarHandle()
    {
        try
        {
            var handle = FindWindow("Shell_TrayWnd", null);
            if (handle == nint.Zero)
            {
                EnsureInteractiveDesktop();
                handle = FindWindow("Shell_TrayWnd", null);
            }
            return handle;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.FindTaskbarHandle failed: {ex.Message}");
            return nint.Zero;
        }
    }

    internal static string GetWindowClassName(nint hwnd)
    {
        try
        {
            var sb = new StringBuilder(256);
            return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.GetWindowClassName failed: {ex.Message}");
            return string.Empty;
        }
    }

    internal static bool IsSecondaryTaskbar(nint hwnd)
    {
        if (hwnd == nint.Zero) return false;
        return string.Equals(GetWindowClassName(hwnd), "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<nint> FindSecondaryTaskbarHandles()
    {
        var list = new List<nint>();
        try
        {
            EnsureInteractiveDesktop();
            nint hwnd = nint.Zero;
            while ((hwnd = FindWindowEx(nint.Zero, hwnd, "Shell_SecondaryTrayWnd", null)) != nint.Zero)
            {
                if (IsWindow(hwnd) && IsWindowVisible(hwnd))
                {
                    list.Add(hwnd);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.FindSecondaryTaskbarHandles failed: {ex.Message}");
        }

        return list;
    }

    internal static IReadOnlyList<nint> FindAllTaskbarHandles()
    {
        var list = new List<nint>();
        var primary = FindTaskbarHandle();
        if (primary != nint.Zero && IsWindow(primary))
        {
            list.Add(primary);
        }

        list.AddRange(FindSecondaryTaskbarHandles());
        return list;
    }

    internal static bool IsClassicTaskbar(nint taskbar = 0)
    {
        try
        {
            if (taskbar == nint.Zero)
            {
                taskbar = FindTaskbarHandle();
            }

            if (taskbar == nint.Zero || !IsWindow(taskbar))
            {
                return false;
            }

            // 1. Direct children fast-check for modern Windows 11 taskbar classes
            nint child = GetWindow(taskbar, GW_CHILD);
            var sb = new StringBuilder(256);
            while (child != nint.Zero)
            {
                sb.Clear();
                if (GetClassName(child, sb, sb.Capacity) > 0)
                {
                    if (IsModernTaskbarClassName(sb.ToString()))
                    {
                        return false;
                    }
                }

                child = GetWindow(child, GW_HWNDNEXT);
            }

            // 2. Deep check: enumerate all descendant windows (handles deeply nested structures in Win11 23H2/24H2)
            bool modernFound = false;
            EnumChildWindows(taskbar, (hwnd, _) =>
            {
                sb.Clear();
                if (GetClassName(hwnd, sb, sb.Capacity) > 0)
                {
                    if (IsModernTaskbarClassName(sb.ToString()))
                    {
                        modernFound = true;
                        return false; // Stop enumeration immediately
                    }
                }

                return true; // Continue enumeration
            }, nint.Zero);

            if (modernFound)
            {
                return false;
            }

            // If a visible classic task list exists without modern XAML components (e.g. ExplorerPatcher/RetroBar on Win11),
            // it is a classic taskbar.
            bool visibleClassicList = false;
            EnumChildWindows(taskbar, (hwnd, _) =>
            {
                sb.Clear();
                if (GetClassName(hwnd, sb, sb.Capacity) > 0 &&
                    string.Equals(sb.ToString(), "MSTaskListWClass", StringComparison.OrdinalIgnoreCase) &&
                    IsWindowVisible(hwnd))
                {
                    visibleClassicList = true;
                    return false;
                }

                return true;
            }, nint.Zero);

            if (visibleClassicList)
            {
                return true;
            }

            return Environment.OSVersion.Version.Build < 22000;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.IsClassicTaskbar failed: {ex.Message}");
            return false;
        }
    }

    internal static bool IsModernTaskbarClassName(string className) =>
        string.Equals(className, TaskbarXamlIslandClass, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "DesktopWindowXamlSource", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "Windows.UI.Input.InputSite.WindowClass", StringComparison.OrdinalIgnoreCase);

    internal static bool IsTaskbarAutoHiding()
    {
        try
        {
            var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
            uint state = SHAppBarMessage(ABM_GETSTATE, ref data);
            return (state & ABS_AUTOHIDE) == ABS_AUTOHIDE;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.IsTaskbarAutoHiding failed: {ex.Message}");
            return false;
        }
    }

    internal static uint GetDpiForWindow(nint hwnd)
    {
        try
        {
            uint dpi = GetDpiForWindowPrivate(hwnd);
            return dpi == 0 ? 96 : dpi;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.GetDpiForWindow failed: {ex.Message}");
            return 96;
        }
    }

    internal static uint GetWindowThreadProcessId(nint hwnd, out uint processId)
    {
        try
        {
            return GetWindowThreadProcessIdPrivate(hwnd, out processId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.GetWindowThreadProcessId failed: {ex.Message}");
            processId = 0;
            return 0;
        }
    }

    internal static bool GetWindowRect(nint hwnd, out RECT rect)
    {
        rect = default;
        try
        {
            return GetWindowRectPrivate(hwnd, out rect);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.GetWindowRect failed: {ex.Message}");
            return false;
        }
    }

    internal static bool TryGetTrayNotifyRect(nint taskbarHandle, out Rect trayRect)
    {
        trayRect = Rect.Empty;
        try
        {
            if (taskbarHandle == nint.Zero)
            {
                return false;
            }

            nint trayNotify = FindWindowEx(taskbarHandle, nint.Zero, "TrayNotifyWnd", null);
            if (trayNotify == nint.Zero || !GetWindowRect(trayNotify, out var r))
            {
                return false;
            }

            int width = r.Right - r.Left;
            int height = r.Bottom - r.Top;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            trayRect = new Rect(r.Left, r.Top, width, height);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.TryGetTrayNotifyRect failed: {ex.Message}");
            return false;
        }
    }

    internal static bool ScreenToClient(nint hwnd, ref POINT pt)
    {
        try
        {
            return ScreenToClientPrivate(hwnd, ref pt);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.ScreenToClient failed: {ex.Message}");
            return false;
        }
    }

    internal static bool TryGetVisibleTaskbarRect(nint taskbarHandle, out Rect taskbarRect)
    {
        taskbarRect = Rect.Empty;
        if (!GetWindowRect(taskbarHandle, out var r))
        {
            return false;
        }

        int taskbarWidth = r.Right - r.Left;
        int taskbarHeight = r.Bottom - r.Top;
        if (taskbarWidth <= 0 || taskbarHeight <= 0)
        {
            return false;
        }

        taskbarRect = new Rect(r.Left, r.Top, taskbarWidth, taskbarHeight);

        if (!IsTaskbarAutoHiding())
        {
            return true;
        }

        var monitor = MonitorFromWindow(taskbarHandle, MONITOR_DEFAULTTONEAREST);
        if (monitor == nint.Zero)
        {
            return true;
        }

        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref mi))
        {
            return true;
        }

        var mon = mi.rcMonitor;
        int visibleWidth = Math.Max(0, Math.Min(r.Right, mon.Right) - Math.Max(r.Left, mon.Left));
        int visibleHeight = Math.Max(0, Math.Min(r.Bottom, mon.Bottom) - Math.Max(r.Top, mon.Top));

        double dpiScale = GetDpiForWindow(taskbarHandle) / 96.0;
        int tolerance = (int)Math.Ceiling(2 * dpiScale);

        bool isVertical = taskbarHeight > taskbarWidth;
        return isVertical ? visibleWidth >= taskbarWidth - tolerance : visibleHeight >= taskbarHeight - tolerance;
    }

    internal static nint SetParent(nint child, nint newParent)
    {
        try
        {
            return SetParentPrivate(child, newParent);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.SetParent failed: {ex.Message}");
            return nint.Zero;
        }
    }

    internal static nint GetParent(nint hwnd)
    {
        try
        {
            return GetParentPrivate(hwnd);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.GetParent failed: {ex.Message}");
            return nint.Zero;
        }
    }

    internal static bool SetWindowPos(nint hwnd, int x, int y, int cx, int cy, uint flags)
    {
        try
        {
            return SetWindowPosPrivate(hwnd, nint.Zero, x, y, cx, cy, flags);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NativeTaskbar.SetWindowPos failed: {ex.Message}");
            return false;
        }
    }
}
