using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace StudyCafe.Companion.Core;

public sealed class AppSettings
{
    private const string AppName = "StudyCafeCompanion";
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName);
    private static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");

    public string SupabaseUrl { get; set; } = "";
    public string SupabaseAnonKey { get; set; } = "";
    public string WebUrl { get; set; } = "http://localhost:5173";
    public int LoopbackPort { get; set; } = 47800;

    private static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }

        var defaults = new AppSettings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public static void OpenConfigFileInEditor()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            if (!File.Exists(SettingsFilePath))
            {
                Current.Save();
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = SettingsFilePath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open config file: {ex.Message}");
        }
    }
}

public static class AutostartHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StudyCafeCompanion";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(ValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set autostart: {ex.Message}");
        }
    }
}
