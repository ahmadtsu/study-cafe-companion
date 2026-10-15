using System.Diagnostics;
using System.IO;
using System.Windows;
using StudyCafe.Companion.Core;
using StudyCafe.Companion.Themes;
using StudyCafe.Companion.Tray;
using StudyCafe.Companion.UI;

namespace StudyCafe.Companion;

public partial class App : Application
{
    private const string MutexName = "Global\\StudyCafeCompanionMutex_ahmadtsu";
    private Mutex? _mutex;
    private ThemeWatcher? _themeWatcher;
    private TimerCountdownEngine? _timerEngine;
    private TaskbarWindow? _taskbarWindow;
    private TrayIconManager? _trayManager;
    private LocalLoopbackService? _loopbackService;
    private SupabaseRealtimeService? _supabaseService;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StudyCafe.Companion", "companion.log");

    public static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"[FATAL] CurrentDomain Unhandled: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"[FATAL] Dispatcher Unhandled: {args.Exception}");
            args.Handled = true; // Prevent crash if recoverable
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log($"[ERROR] UnobservedTask: {args.Exception}");
            args.SetObserved();
        };

        Log("App starting...");

        try
        {
            if (e.Args.Contains("--restart") || e.Args.Contains("-r"))
            {
                Log("Restart flag detected, terminating existing companion instances...");
                foreach (var p in Process.GetProcessesByName("StudyCafe.Companion"))
                {
                    if (p.Id != Environment.ProcessId)
                    {
                        try { p.Kill(); p.WaitForExit(1000); } catch { }
                    }
                }
            }

            _mutex = new Mutex(true, MutexName, out bool isNewInstance);
            Log($"Mutex acquired: isNewInstance={isNewInstance}");
            if (!isNewInstance)
            {
                Log("Another instance is already running. Notifying user and shutting down.");
                System.Windows.MessageBox.Show(
                    "Study Cafe Companion is already running in your Windows taskbar (next to the system tray chevron).\n\nTo restart it, right-click its widget or tray icon and choose 'Exit', or run with --restart.",
                    "Study Cafe Companion",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }
        catch (AbandonedMutexException)
        {
            Log("Mutex was abandoned by previous crashed process. Continuing.");
        }
        catch (Exception ex)
        {
            Log($"Mutex exception: {ex.Message}");
        }

        base.OnStartup(e);

        try
        {
            // Initialize Settings
            var settings = AppSettings.Current;
            Log("Settings loaded");

            // Initialize Themes
            _themeWatcher = new ThemeWatcher();
            ApplyTheme(_themeWatcher.Current.IsLight);
            _themeWatcher.Changed += (_, snapshot) => Dispatcher.Invoke(() => ApplyTheme(snapshot.IsLight));
            Log("ThemeWatcher initialized");

            // Initialize Timer Engine
            _timerEngine = new TimerCountdownEngine();
            Log("TimerCountdownEngine initialized");

            // Initialize UI Windows
            _taskbarWindow = new TaskbarWindow();
            _taskbarWindow.Initialize();
            Log("TaskbarWindow initialized and attached");

            // Initialize System Tray
            _trayManager = new TrayIconManager();
            Log("TrayIconManager initialized");

            // Hook timer updates to UI and Tray
            _timerEngine.StateChanged += (formattedTime, phase, participants, isPaused) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _taskbarWindow.UpdateTimerState(formattedTime, phase, participants, isPaused);
                    _trayManager.UpdateStatus(formattedTime, phase, participants, isPaused);
                });
            };

            // Initialize Local Loopback (HTTP port 47800)
            _loopbackService = new LocalLoopbackService(_timerEngine, settings.LoopbackPort);
            Log($"LocalLoopbackService started on port {settings.LoopbackPort}");

            // Initialize Supabase Realtime Service
            _supabaseService = new SupabaseRealtimeService(_timerEngine);
            _ = Task.Run(async () =>
            {
                try
                {
                    await _supabaseService.StartAsync();
                }
                catch (Exception ex)
                {
                    Log($"Failed to start Supabase realtime: {ex.Message}");
                }
            });
            Log("App startup completed successfully");
        }
        catch (Exception ex)
        {
            Log($"Fatal startup error: {ex}");
            System.Windows.MessageBox.Show($"Study Cafe Companion encountered an error during startup:\n{ex.Message}",
                "Study Cafe Companion", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void ApplyTheme(bool isLight)
    {
        try
        {
            var dictUri = new Uri(isLight ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative);
            var newDict = new ResourceDictionary { Source = dictUri };

            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(newDict);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ApplyTheme error: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _supabaseService?.Dispose();
        _loopbackService?.Dispose();
        _trayManager?.Dispose();
        _themeWatcher?.Dispose();

        if (_mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
            _mutex.Dispose();
        }

        base.OnExit(e);
    }
}
