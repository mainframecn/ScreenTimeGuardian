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
    // countdown card instead; no clamp applies.
    private const int WarningSeconds = 120;
    private const int BreakStartSeconds = 60;

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

        // Persistent countdown card: top-right window with a live "locks in Ns"
        // counter, click-to-dismiss, auto-close at 0. Balloon tips cap at 30 s
        // which is too short to act on; a real window has no such limit.
        // TopMost alone loses z-order over fullscreen apps (games), so the loop
        // re-asserts it every half-second tick.
        var script = string.Join(Environment.NewLine, [
            "Add-Type -AssemblyName System.Windows.Forms,System.Drawing",
            "$f = New-Object System.Windows.Forms.Form",
            "$f.Text = 'ScreenTime Guardian'",
            "$f.StartPosition = 'Manual'",
            "$f.TopMost = $true",
            "$f.BackColor = [System.Drawing.Color]::FromArgb(30,30,30)",
            "$lbl = New-Object System.Windows.Forms.Label",
            "$lbl.AutoSize = $true; $lbl.ForeColor = [System.Drawing.Color]::White",
            "$lbl.Font = [System.Drawing.Font]::New('Segoe UI', 14)",
            "$sub = New-Object System.Windows.Forms.Label",
            "$sub.AutoSize = $true; $sub.ForeColor = [System.Drawing.Color]::FromArgb(200,200,200)",
            "$lbl.Location = [System.Drawing.Point]::New(12, 8); $sub.Location = [System.Drawing.Point]::New(12, 34)",
            "$f.Controls.AddRange([System.Windows.Forms.Control[]]($lbl,$sub))",
            $"$title = '{QuoteForPowerShell(title)}'; $msg = '{QuoteForPowerShell(message)}'",
            "$lbl.Text = $title; $sub.Text = $msg",
            "$w = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds",
            "$f.ClientSize = [System.Drawing.Size]::New(420, 78)",
            "$f.Location = [System.Drawing.Point]::New($w.Width - 440, 20)",
            "$left = " + seconds,
            "$closeBtn = New-Object System.Windows.Forms.Button",
            "$closeBtn.Text = 'x'; $closeBtn.ForeColor = [System.Drawing.Color]::White",
            "$closeBtn.BackColor = [System.Drawing.Color]::FromArgb(30,30,30)",
            "$closeBtn.Location = [System.Drawing.Point]::New(384, 8); $closeBtn.Size = [System.Drawing.Size]::New(26,26)",
            "$closeBtn.Font = [System.Drawing.Font]::New('Segoe UI', 12)",
            "$f.Controls.Add($closeBtn)",
            "$f.Show()",
            "while ($left -gt 0) {",
            "  $f.TopMost = $true",
            "  $lbl.Text = $title",
            "  $sub.Text = ('{0}  |  locks in {1}s' -f $msg, $left)",
            "  Start-Sleep -Milliseconds 500",
            "  $left--",
            "}",
            "if (-not $f.IsDisposed) { $f.Close() }"
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

            var exited = process.WaitForExit((seconds + 15) * 1000);
            GuardLog.Info($"notification card shown for {seconds}s exited={exited} code={process.ExitCode}");
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
