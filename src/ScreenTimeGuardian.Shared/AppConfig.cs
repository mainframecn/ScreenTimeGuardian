using System.IO;
using System.Text.Json;

namespace ScreenTimeGuardian.Shared;

public class AppConfig
{
    public int UsageDurationMinutes { get; set; } = 30;
    public int BreakDurationMinutes { get; set; } = 15;
    public string ParentPin { get; set; } = "123456";
    public bool NotificationsEnabled { get; set; } = true;
    public int NotificationWarningMinutes { get; set; } = 5;
    public int NotificationUrgentMinutes { get; set; } = 1;

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ScreenTimeGuardian");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public static AppConfig Load()
    {
        // A half-written config.json (crash mid-save) must not take the service
        // down with it: an unhandled exception here becomes a watchdog-driven
        // crash loop. Fall back to defaults and say so in the log instead.
        try
        {
            if (!File.Exists(ConfigPath))
            {
                var defaultConfig = new AppConfig();
                defaultConfig.Save();
                return defaultConfig;
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            GuardLog.Error($"config load failed, using defaults ({ConfigPath})", ex);
            return new AppConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

        // Write-temp-then-rename: a crash mid-write must leave either the old
        // file or the new one, never a truncated one.
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(ConfigPath))
        {
            File.Replace(tmp, ConfigPath, null);
        }
        else
        {
            File.Move(tmp, ConfigPath);
        }
    }

    public static string GetConfigPath() => ConfigPath;

    // Where the lock screen executable lives. Prefer the folder of the running
    // service; fall back to the installed folder so running the service from a
    // build directory during development still finds the installed window.
    public static string GetLockScreenPath()
    {
        const string exeName = "ScreenTimeGuardian.LockScreen.exe";

        var candidates = new List<string>();

        var appDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(appDir))
        {
            candidates.Add(Path.Combine(appDir, exeName));
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ScreenTimeGuardian", exeName));

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates.Count > 0 ? candidates[0] : exeName;
    }
}

