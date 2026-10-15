// Adapted from EverythingToolbar (MIT © 2023 Stephan Rumswinkel). See ATTRIBUTION.md.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace StudyCafe.Companion.Themes;

public sealed record ThemeSnapshot(bool IsLight, Color Accent);

/// <summary>
/// Watches HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
/// for system/apps light/dark theme switches and reports the DWM accent color.
/// </summary>
internal sealed class ThemeWatcher : IDisposable
{
    private const string PersonalizeKeyPath =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string PersonalizeSubKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightValueName = "SystemUsesLightTheme";

    private static readonly IntPtr HKEY_CURRENT_USER = new(0x80000001);
    private const int KEY_NOTIFY = 0x0010;
    private const int REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegOpenKeyEx(
        IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        IntPtr hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        int dwNotifyFilter,
        IntPtr hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetColorizationColor(
        out uint pcrColorization,
        [MarshalAs(UnmanagedType.Bool)] out bool pfOpaqueBlend);

    private readonly object _gate = new();
    private readonly SynchronizationContext? _syncContext;
    private readonly Thread _watchThread;
    private readonly ManualResetEvent _stopEvent = new(false);
    private readonly ManualResetEvent _notifyEvent = new(false);
    private readonly System.Threading.Timer _debounce;
    private ThemeSnapshot _current;
    private bool _disposed;

    public ThemeWatcher()
    {
        _syncContext = SynchronizationContext.Current;
        _current = ReadSnapshot();
        _debounce = new System.Threading.Timer(OnDebounced, null, Timeout.Infinite, Timeout.Infinite);
        _watchThread = new Thread(WatchLoop) { IsBackground = true, Name = nameof(ThemeWatcher) };
        _watchThread.SetApartmentState(ApartmentState.MTA);
        _watchThread.Start();
    }

    public ThemeSnapshot Current
    {
        get { lock (_gate) return _current; }
    }

    public void Refresh() => OnDebounced(null);

    public event EventHandler<ThemeSnapshot>? Changed;

    private void WatchLoop()
    {
        try
        {
            if (RegOpenKeyEx(HKEY_CURRENT_USER, PersonalizeSubKey, 0, KEY_NOTIFY, out IntPtr hKey) != 0)
            {
                Debug.WriteLine("ThemeWatcher: RegOpenKeyEx failed; theme changes will not be observed.");
                return;
            }

            try
            {
                var handles = new WaitHandle[] { _stopEvent, _notifyEvent };
                while (!_disposed)
                {
                    _notifyEvent.Reset();
                    int rc = RegNotifyChangeKeyValue(
                        hKey,
                        false,
                        REG_NOTIFY_CHANGE_LAST_SET,
                        _notifyEvent.SafeWaitHandle.DangerousGetHandle(),
                        true);
                    if (rc != 0)
                    {
                        Debug.WriteLine($"ThemeWatcher: RegNotifyChangeKeyValue failed ({rc}).");
                        return;
                    }

                    if (WaitHandle.WaitAny(handles) == 0 || _disposed)
                        return;

                    try
                    {
                        _debounce.Change(500, Timeout.Infinite);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            }
            finally
            {
                RegCloseKey(hKey);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ThemeWatcher: {ex}");
        }
    }

    private void OnDebounced(object? _)
    {
        ThemeSnapshot snapshot;
        try
        {
            snapshot = ReadSnapshot();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ThemeWatcher: {ex}");
            return;
        }

        lock (_gate)
        {
            _current = snapshot;
        }

        var handler = Changed;
        if (handler is null) return;

        try
        {
            if (_syncContext is not null)
                _syncContext.Post(static state =>
                {
                    var (watcher, snap, callback) = (ValueTuple<ThemeWatcher, ThemeSnapshot, EventHandler<ThemeSnapshot>>)state!;
                    callback(watcher, snap);
                }, (this, snapshot, handler));
            else
                handler(this, snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ThemeWatcher: {ex}");
        }
    }

    private static ThemeSnapshot ReadSnapshot() => new(IsLightTheme(), GetAccentColor());

    private static bool IsLightTheme()
    {
        try
        {
            object? value = Registry.GetValue(PersonalizeKeyPath, LightValueName, 0);
            return Convert.ToInt32(value) == 1;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ThemeWatcher: IsLightTheme fallback: {ex.Message}");
            return false;
        }
    }

    private static Color GetAccentColor()
    {
        try
        {
            int hr = DwmGetColorizationColor(out uint colorization, out bool _);
            if (hr == 0)
            {
                byte r = (byte)((colorization >> 16) & 0xFF);
                byte g = (byte)((colorization >> 8) & 0xFF);
                byte b = (byte)(colorization & 0xFF);
                return Color.FromRgb(r, g, b);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ThemeWatcher: accent fallback: {ex.Message}");
        }

        return Colors.DimGray;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _stopEvent.Set(); } catch { }
        try { _watchThread.Join(TimeSpan.FromSeconds(2)); } catch { }
        try { _debounce.Dispose(); } catch { }
        try { _stopEvent.Dispose(); } catch { }
        try { _notifyEvent.Dispose(); } catch { }
    }
}
