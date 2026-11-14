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
        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new AppConfig();
            defaultConfig.Save();
            return defaultConfig;
        }

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
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

