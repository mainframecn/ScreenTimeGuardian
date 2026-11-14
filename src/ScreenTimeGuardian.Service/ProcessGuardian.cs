using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Shared;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenTimeGuardian.Service;

public class ProcessGuardian
{
    private readonly ILogger? _logger;
    private System.Threading.Timer? _guardianTimer;
    private bool _isProtecting;

    private const string ServiceName = "ScreenTimeGuardian";

    public ProcessGuardian(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void StartProtection()
    {
        if (_isProtecting) return;
        _isProtecting = true;

        DisableTaskManager();
        DisableShutdownMenu();

        _guardianTimer = new System.Threading.Timer(GuardianTick, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        _logger?.LogInformation("Process protection started.");
    }

    public void StopProtection()
    {
        _isProtecting = false;
        _guardianTimer?.Dispose();
        EnableTaskManager();
        EnableShutdownMenu();
        _logger?.LogInformation("Process protection stopped.");
    }

    private void GuardianTick(object? state)
    {
        if (!_isProtecting) return;

        try
        {
            DisableTaskManager();
            DisableShutdownMenu();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in guardian tick.");
        }
    }

    // Machine-level hive on purpose: this code runs as the LocalSystem service
    // account, where HKCU resolves to the SYSTEM profile and therefore has no
    // effect on the signed-in child's session. taskmgr.exe and explorer.exe read
    // both hives, so HKLM is the one that reaches the login session.
    private const string PoliciesSystem = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string PoliciesExplorer = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    private static Microsoft.Win32.RegistryKey OpenPolicyKey(string path)
    {
        return Microsoft.Win32.Registry.LocalMachine.CreateSubKey(path);
    }

    private void DisableTaskManager()
    {
        try
        {
            using var key = OpenPolicyKey(PoliciesSystem);
            key?.SetValue("DisableTaskMgr", 1, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to disable Task Manager.");
        }
    }

    private void EnableTaskManager()
    {
        try
        {
            using var key = OpenPolicyKey(PoliciesSystem);
            key?.DeleteValue("DisableTaskMgr", false);
        }
        catch { }
    }

    private void DisableShutdownMenu()
    {
        try
        {
            using var key = OpenPolicyKey(PoliciesExplorer);
            key?.SetValue("NoClose", 1, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to disable shutdown menu.");
        }
    }

    private void EnableShutdownMenu()
    {
        try
        {
            using var key = OpenPolicyKey(PoliciesExplorer);
            key?.DeleteValue("NoClose", false);
        }
        catch { }
    }

    // A reboot or crash mid-break can leave the machine-level policy values
    // behind with nothing left to clear them, so wipe them at startup: an
    // interrupted break must not trap the machine in a permanent lockout.
    public void ClearStaleProtection()
    {
        EnableTaskManager();
        EnableShutdownMenu();
        _logger?.LogInformation("Cleared any protection policy left over from a previous run.");
    }
}
