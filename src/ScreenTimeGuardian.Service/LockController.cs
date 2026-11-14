using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Shared;
using System.Diagnostics;

namespace ScreenTimeGuardian.Service;

public class LockController
{
    private readonly ILogger? _logger;
    private Process? _lockScreenProcess;

    public bool IsLocked { get; private set; }

    public LockController(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void Lock(AppConfig config)
    {
        if (IsLocked)
        {
            _logger?.LogWarning("Already locked, ignoring lock request.");
            return;
        }

        try
        {
            var lockScreenPath = AppConfig.GetLockScreenPath();

            if (!File.Exists(lockScreenPath))
            {
                _logger?.LogError("LockScreen executable not found at {Path}", lockScreenPath);
                return;
            }

            _logger?.LogInformation("Launching lock screen: {Path}", lockScreenPath);

            // A GUI process started directly by a Windows service runs in session 0,
            // where Windows blocks the window and the process dies within seconds
            // (observed: "activated" then deactivated 17 s later with no window ever
            // drawn). Launch through a one-shot scheduled task running as the
            // interactive console user instead; /run starts it immediately.
            var viaTask = LaunchViaTask(lockScreenPath, config.BreakDurationMinutes);
            if (!viaTask)
            {
                // Fallback: direct launch (works for dev-run/console mode).
                _lockScreenProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = lockScreenPath,
                    Arguments = $"--duration {config.BreakDurationMinutes}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }

            IsLocked = true;
            _logger?.LogInformation("Lock screen activated.");

            // A GUI process started by a service runs in session 0, where Windows
            // blocks the window and the process dies within a second or two.
            // Recording the exit code here means a field test leaves evidence in
            // the event log instead of relying on someone noticing a blank screen.
            var launched = _lockScreenProcess;
            if (launched != null && launched.WaitForExit(2000))
            {
                _logger?.LogError(
                    "Lock screen exited immediately with code {ExitCode}; the window never appeared.",
                    launched.ExitCode);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to launch lock screen.");
        }
    }

    // One-shot scheduled task launch: GUI processes started by a session-0 service
    // are blocked from drawing windows, so the overlay runs under the interactive
    // console user's account via schtasks. /ru <user> needs that name; WMI Win32_ComputerSystem
    // reports it. Task is deleted after /run so it never re-fires.
    private const string TASK_NAME = "ScreenTimeGuardianLockOnce";

    private bool LaunchViaTask(string exePath, int durationMinutes)
    {
        try
        {
            var user = QueryConsoleUser();
            if (string.IsNullOrEmpty(user))
            {
                _logger?.LogWarning("Could not determine console user; task launch skipped.");
                return false;
            }

            var tr = $"\"{exePath}\" --duration {durationMinutes}";
            var create = RunSchtasks($"/create /tn \"{TASK_NAME}\" /tr \"{tr}\" /ru {user} /f");
            if (create == null || !create.Contains("SUCCESS"))
            {
                _logger?.LogWarning("Task create failed: {Out}", create ?? "<null>");
                return false;
            }

            var run = RunSchtasks($"/run /tn \"{TASK_NAME}\"");
            _logger?.LogInformation("Lock screen launched via task as {User}: {Run}", user, run ?? "<null>");

            // Delete after running so the task cannot re-fire on schedule.
            RunSchtasks($"/delete /tn \"{TASK_NAME}\" /f");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Task launch failed.");
            return false;
        }
    }

    private string? QueryConsoleUser()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command (gwim Win32_ComputerSystem).UserName",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            if (p == null) return null;
            p.WaitForExit(5000);
            var name = (p.StandardOutput.ReadToEnd() ?? string.Empty).Trim();
            // UserName comes back as "DOMAIN\\user"; schtasks wants the bare account.
            var bs = name.LastIndexOf('\\');
            return bs >= 0 ? name.Substring(bs + 1) : (name.Length > 0 ? name : null);
        }
        catch
        {
            return null;
        }
    }

    private string? RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            if (p == null) return null;
            p.WaitForExit(8000);
            return p.StandardOutput.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    public void Unlock()
    {
        if (!IsLocked)
        {
            _logger?.LogWarning("Not locked, ignoring unlock request.");
            return;
        }

        try
        {
            if (_lockScreenProcess != null && !_lockScreenProcess.HasExited)
            {
                _logger?.LogInformation("Closing lock screen process.");
                _lockScreenProcess.Kill();
                _lockScreenProcess.WaitForExit(5000);
            }
            _lockScreenProcess?.Dispose();
            _lockScreenProcess = null;
            IsLocked = false;
            _logger?.LogInformation("Lock screen deactivated.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to close lock screen.");
            IsLocked = false;
        }
    }
}
