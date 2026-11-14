using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Shared;

namespace ScreenTimeGuardian.Service;

public class ProcessGuardian
{
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private System.Threading.Timer? _guardianTimer;
    private bool _isProtecting;

    // What the policy values were BEFORE this run touched them, captured in
    // StartProtection so StopProtection can put back exactly what was there —
    // a machine that already had DisableTaskMgr set (domain policy, other
    // parental software) must get its own value back, not a blind delete.
    // null = the value did not exist (delete on restore). Only meaningful
    // while _isProtecting; startup cleanup after a crash cannot know the prior
    // value and stays a wipe (see ClearStaleProtection).
    private int? _priorDisableTaskMgr;
    private int? _priorNoClose;

    private const string PoliciesSystem = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string PoliciesExplorer = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    public ProcessGuardian(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void StartProtection()
    {
        lock (_gate)
        {
            if (_isProtecting) return;
            _isProtecting = true;

            _priorDisableTaskMgr = ReadValue(PoliciesSystem, "DisableTaskMgr");
            _priorNoClose = ReadValue(PoliciesExplorer, "NoClose");

            DisableTaskManager();
            DisableShutdownMenu();

            _guardianTimer = new System.Threading.Timer(GuardianTick, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        _logger?.LogInformation("Process protection started.");
    }

    public void StopProtection()
    {
        lock (_gate)
        {
            if (!_isProtecting)
            {
                // Idempotent for repeated unlocks, but never touch the policy
                // when this run did not set it.
                return;
            }

            _isProtecting = false;
            _guardianTimer?.Dispose();
            _guardianTimer = null;

            RestoreValue(PoliciesSystem, "DisableTaskMgr", _priorDisableTaskMgr);
            RestoreValue(PoliciesExplorer, "NoClose", _priorNoClose);
            _priorDisableTaskMgr = null;
            _priorNoClose = null;
        }

        _logger?.LogInformation("Process protection stopped.");
    }

    private void GuardianTick(object? state)
    {
        // The lock closes the race the old code had: StopProtection could
        // clear the policy while a tick already past its _isProtecting check
        // re-set it, leaving Task Manager disabled with no timer left to fix
        // it. Tick and stop now serialize on _gate.
        lock (_gate)
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
    }

    // Machine-level hive on purpose: this code runs as the LocalSystem service
    // account, where HKCU resolves to the SYSTEM profile and therefore has no
    // effect on the signed-in child's session. taskmgr.exe and explorer.exe read
    // both hives, so HKLM is the one that reaches the login session.
    private static Microsoft.Win32.RegistryKey OpenPolicyKey(string path)
    {
        return Microsoft.Win32.Registry.LocalMachine.CreateSubKey(path);
    }

    private static int? ReadValue(string path, string name)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue(name) as int?;
        }
        catch
        {
            // Unreadable counts as "we don't know": restore deletes, which
            // matches the pre-fix behavior and can never trap the machine.
            return null;
        }
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

    private void RestoreValue(string path, string name, int? prior)
    {
        try
        {
            using var key = OpenPolicyKey(path);
            if (key is null) return;

            if (prior.HasValue)
            {
                key.SetValue(name, prior.Value, Microsoft.Win32.RegistryValueKind.DWord);
            }
            else
            {
                key.DeleteValue(name, false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to restore policy value {Name}.", name);
        }
    }

    // A reboot or crash mid-break can leave the machine-level policy values
    // behind with nothing left to clear them, so wipe them at startup: an
    // interrupted break must not trap the machine in a permanent lockout.
    // The prior value is unknowable after a crash, so this deliberately does
    // NOT try to restore — the symmetric Start/Stop path above handles the
    // clean case.
    public void ClearStaleProtection()
    {
        lock (_gate)
        {
            try
            {
                using (var key = OpenPolicyKey(PoliciesSystem))
                {
                    key?.DeleteValue("DisableTaskMgr", false);
                }
                using (var key = OpenPolicyKey(PoliciesExplorer))
                {
                    key?.DeleteValue("NoClose", false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to clear stale protection policy.");
            }
        }

        _logger?.LogInformation("Cleared any protection policy left over from a previous run.");
    }
}
