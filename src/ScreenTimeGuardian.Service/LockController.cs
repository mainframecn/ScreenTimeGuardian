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
            // where Windows blocks the window (observed: process alive, no window
            // ever drawn on the desktop). Launch with our SYSTEM token relocated to
            // the interactive console session instead.
            _lockScreenProcess = SessionLauncher.Launch(lockScreenPath, $"--duration {config.BreakDurationMinutes}");
            if (_lockScreenProcess == null)
            {
                // Fallback: direct launch (works for dev-run/console mode).
                _logger?.LogWarning("Session launch unavailable; falling back to direct start.");
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
