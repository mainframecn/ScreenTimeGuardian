using System.ServiceProcess;

namespace ScreenTimeGuardian.Config;

/// <summary>
/// Lets the Config app release and restart the running service. config.json
/// lives in a folder the service (LocalSystem) writes, so a save that hits a
/// locked file can be fixed by stopping the service first, retrying the write,
/// and starting it again - the start also loads the new config.
/// </summary>
public static class ServiceControl
{
    private const string ServiceName = "ScreenTimeGuardian";
    private const string ProcessName = "ScreenTimeGuardian.Service";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Stop the service if it is running. Returns true when the service is
    /// (now) not running. Missing service is treated as "not running".
    /// </summary>
    public static bool TryStop()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Stopped)
                return true;

            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, Timeout);
            return true;
        }
        catch
        {
            // Service not installed, refuses to stop, or SCM is unhappy -
            // fall back to killing the process, then re-check.
            return KillProcessIfRunning();
        }
    }

    /// <summary>
    /// Start the service again if it is stopped. Safe when already running
    /// (the watchdog may restart it first) or when it is not installed.
    /// </summary>
    public static void Start()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status != ServiceControllerStatus.Stopped)
                return;

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, Timeout);
        }
        catch
        {
            // Best effort: the watchdog restarts the service within a minute
            // anyway, and the config file is already written.
        }
    }

    private static bool KillProcessIfRunning()
    {
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName(ProcessName))
            {
                proc.Kill(true);
                proc.WaitForExit(3000);
            }

            return System.Diagnostics.Process.GetProcessesByName(ProcessName).Length == 0;
        }
        catch
        {
            return false;
        }
    }
}
