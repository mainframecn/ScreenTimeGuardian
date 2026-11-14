using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Shared;

namespace ScreenTimeGuardian.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TimerManager _timerManager;
    private readonly LockController _lockController;
    private readonly ProcessGuardian _processGuardian;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _timerManager = new TimerManager(logger);
        _lockController = new LockController(logger);
        _processGuardian = new ProcessGuardian(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScreenTimeGuardian service starting...");

        var config = AppConfig.Load();
        _logger.LogInformation("Config loaded: Usage={Usage}min, Break={Break}min",
            config.UsageDurationMinutes, config.BreakDurationMinutes);

        // Protection is scoped to the break window only, so any values left behind
        // by a reboot or crash mid-break are cleared before the timers run.
        _processGuardian.ClearStaleProtection();

        // Commands are queued in files on disk; drop anything left over from a
        // previous run so a stale marker cannot lock or unlock us at startup.
        IpcMessages.CleanupCommands();

        _timerManager.OnUsageTimeReached += () =>
        {
            _logger.LogWarning("Usage time reached! Triggering lock screen.");
            _processGuardian.StartProtection();
            NotificationManager.ShowBreakStart(config.BreakDurationMinutes, _logger);
            _lockController.Lock(config);
        };

        _timerManager.OnBreakTimeReached += () =>
        {
            _logger.LogInformation("Break time over. Unlocking screen.");
            _processGuardian.StopProtection();
            _lockController.Unlock();
        };

        _timerManager.OnNotificationWarning += (minutes) =>
        {
            _logger.LogInformation("Break in {Minutes} minutes warning sent.", minutes);
            NotificationManager.ShowWarning(minutes, _logger);
        };

        _timerManager.Start(config);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var command = IpcMessages.WaitForCommand(500);
                if (command == IpcMessages.PIN_REQUEST)
                {
                    // Overlay asked for the PIN; reply with a one-shot grant file.
                    // The PIN never travels on any command line, so process listings
                    // and screenshots cannot leak it.
                    try
                    {
                        Directory.CreateDirectory(Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                            "ScreenTimeGuardian", "ipc"));
                        var grantPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                            "ScreenTimeGuardian", "ipc",
                            $"{IpcMessages.PIN_GRANT}_{DateTime.UtcNow:yyyyMMddHHmmssfffffff}.txt");
                        File.WriteAllText(grantPath, config.ParentPin);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to write PIN grant.");
                    }
                }
                else if (command == IpcMessages.UNLOCK_SCREEN_CMD)
                {
                    _logger.LogInformation("Parent override unlock received.");
                    // Tear down any live countdown card: a running "locks in Ns"
                    // after an unlock lies to the child about being locked.
                    NotificationManager.CancelCards();
                    _processGuardian.StopProtection();
                    // Parent PIN unlock = "let the child play now": idle exclusion
                    // off, so gamepad/movie sessions cannot mint free time. The
                    // next boot or reset re-enables it.
                    _timerManager.ResetTimers(excludeIdleTime: false);
                    _lockController.Unlock();
                }
                else if (command == IpcMessages.LOCK_SCREEN_CMD)
                {
                    _logger.LogInformation("Manual lock triggered.");
                    _processGuardian.StartProtection();
                    _lockController.Lock(config);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing IPC command.");
            }

            await Task.Delay(100, stoppingToken);
        }

        _timerManager.Stop();
        _processGuardian.StopProtection();
        _logger.LogInformation("ScreenTimeGuardian service stopped.");
    }
}
