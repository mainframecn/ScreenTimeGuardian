using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Shared;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ScreenTimeGuardian.Service;

public static class NotificationManager
{
    // Balloon tips die at 30 s (Windows clamps them), which users reported as
    // too short to act on. These are display durations for a persistent
    // countdown card instead; no clamp applies. Warning cards now count down
    // the real seconds-to-lock passed in by TimerManager, not a fixed span.
    private const int BreakStartSeconds = 60;

    public static void ShowWarning(int minutes, int secondsLeft, ILogger? logger = null)
    {
        try
        {
            var title = minutes == 1 ? "Break in 1 minute!" : $"Break in {minutes} minutes";
            var message = "Save your work. The computer will lock soon for a break.";

            // Countdown reflects the actual time to lock, so the card's timer
            // and its headline agree (was hard-coded at 120 s).
            SendWindowsNotification(title, message, Math.Max(5, secondsLeft));
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

    // Pre-lock countdown card. The PowerShell/WinForms variant never became
    // visible on the desktop from the service (see git history), while the
    // LockScreen WPF exe launched through SessionLauncher renders reliably -
    // so the card is now a --card mode of that same executable, sharing its
    // proven session/desktop path. The card compares its start time against
    // this sentinel every tick and exits if a cancel landed after it started,
    // so a later card is never killed by an older cancel.
    private static readonly string CardCancelFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ScreenTimeGuardian", "ipc", "STG_CARD_CANCEL");

    public static void CancelCards()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CardCancelFile)!);
            File.WriteAllText(CardCancelFile, DateTime.UtcNow.ToString("o"));
        }
        catch (Exception ex)
        {
            GuardLog.Warn($"could not write card cancel sentinel: {ex.Message}");
        }
    }

    private static void ShowBalloon(string title, string message, int seconds)
    {
        try
        {
            var cardHost = AppConfig.GetLockScreenPath();
            if (!File.Exists(cardHost))
            {
                GuardLog.Warn($"card host missing at {cardHost}");
                return;
            }

            var arguments = $"--card --seconds {seconds} --title \"{title}\" --message \"{message}\"";

            // Same session/desktop rules as the overlay itself.
            var process = SessionLauncher.Launch(cardHost, arguments)
                ?? Process.Start(new ProcessStartInfo
                {
                    FileName = cardHost,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

            if (process == null)
            {
                GuardLog.Warn("Notification card could not be started.");
                return;
            }

            var exited = process.WaitForExit((seconds + 15) * 1000);
            // ExitCode throws for a Process obtained via GetProcessById
            // (SessionLauncher path) - guard it or the outcome never logs.
            int code;
            try { code = process.ExitCode; } catch { code = -1; }
            GuardLog.Info($"notification card shown for {seconds}s exited={exited} code={code}");
        }
        catch (Exception ex)
        {
            GuardLog.Error("Notification failed", ex);
        }
    }
}
