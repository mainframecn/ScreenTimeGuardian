using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Shared;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ScreenTimeGuardian.Service;

public static class NotificationManager
{
    // Windows clamps a balloon tip lifetime between 10 and 30 seconds, so these
    // are the readable limits rather than a timeout that looks configurable but
    // is ignored. https://stackoverflow.com/questions/6846112
    private const int WarningSeconds = 20;
    private const int BreakStartSeconds = 30;

    public static void ShowWarning(int minutes, ILogger? logger = null)
    {
        try
        {
            var title = minutes == 1 ? "Break in 1 minute!" : $"Break in {minutes} minutes";
            var message = "Save your work. The computer will lock soon for a break.";

            SendWindowsNotification(title, message, WarningSeconds);
            logger?.LogInformation("Notification sent: {Title}", title);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to send notification.");
        }
    }

    public static void ShowBreakStart(int breakMinutes, ILogger? logger = null)
    {
        try
        {
            var title = "Time for a break!";
            var message = $"The computer is locked for {breakMinutes} minutes. Take a break!";

            SendWindowsNotification(title, message, BreakStartSeconds);
            logger?.LogInformation("Break notification sent.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to send break notification.");
        }
    }

    // Runs on its own task: the balloon needs to stay up for the whole
    // duration, and the service loop must not sit waiting for it.
    private static void SendWindowsNotification(string title, string message, int seconds)
    {
        _ = Task.Run(() => ShowBalloon(title, message, seconds));
    }

    private static void ShowBalloon(string title, string message, int seconds)
    {
        var scriptPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ScreenTimeGuardian", "toast.ps1");

        var clamped = Math.Clamp(seconds, 10, 30);
        var script = string.Join(Environment.NewLine, [
            "Add-Type -AssemblyName System.Windows.Forms",
            "$notify = New-Object System.Windows.Forms.NotifyIcon",
            "$notify.Icon = [System.Drawing.SystemIcons]::Information",
            "$notify.Visible = $true",
            $"$notify.BalloonTipTitle = '{QuoteForPowerShell(title)}'",
            $"$notify.BalloonTipText = '{QuoteForPowerShell(message)}'",
            "$notify.BalloonTipIcon = 'Info'",
            $"$notify.ShowBalloonTip({clamped * 1000})",
            $"Start-Sleep -Seconds {clamped}",
            "$notify.Dispose()"
        ]);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            File.WriteAllText(scriptPath, script);

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
            {
                GuardLog.Warn("Notification process could not be started.");
                return;
            }

            var exited = process.WaitForExit((clamped + 15) * 1000);
            GuardLog.Info($"notification balloon shown for {clamped}s exited={exited} code={process.ExitCode}");
        }
        catch (Exception ex)
        {
            GuardLog.Error("Notification failed", ex);
        }
    }

    // Settings come from an editable file, so the text must not be able to close
    // the PowerShell string and run something of its own.
    private static string QuoteForPowerShell(string value)
        => value.Replace("'", "''");
}
