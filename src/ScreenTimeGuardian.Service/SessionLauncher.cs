using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ScreenTimeGuardian.Shared;

namespace ScreenTimeGuardian.Service;

// Windows isolates session 0: a GUI child of a service never reaches the
// desktop, so the afternoon dev-run test (console session) worked while the
// installed service's overlay and notification cards were invisible. The fix
// used by parental-control and kiosk software: duplicate our own SYSTEM token,
// move the copy to the active console session (SeTcbPrivilege, which SYSTEM
// holds) and start the child with it - the child keeps SYSTEM rights
// (ProgramData IPC dir, PIN grant files, HKLM policy) but draws in the user's
// session. schtasks /ru <user> was not an option: SYSTEM cannot create a task
// for another account without that account's password.
internal static class SessionLauncher
{
    private const uint TOKEN_ALL_ACCESS = 0xF01FF;
    private const int TOKEN_SESSION_ID = 12; // TOKEN_INFORMATION_CLASS.TokenSessionId (23 would be TokenVirtualizationAllowed)
    private const uint SE_PRIVILEGE_ENABLED = 0x2;
    private const int SECURITY_IMPERSONATION = 2; // SecurityImpersonation
    private const int TOKEN_PRIMARY = 1;
    private const uint STARTF_USESHOWWINDOW = 0x1;
    private const short SW_HIDE = 0;

    public static Process? Launch(string exePath, string arguments)
    {
        IntPtr processToken = IntPtr.Zero;
        IntPtr primary = IntPtr.Zero;
        try
        {
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                GuardLog.Warn("session launch: no active console session");
                return null;
            }

            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ALL_ACCESS, out processToken))
            {
                GuardLog.Warn($"session launch: OpenProcessToken failed err={Marshal.GetLastWin32Error()}");
                return null;
            }

            // SYSTEM holds these but they may be disabled on the token copy.
            EnablePrivilege(processToken, "SeTcbPrivilege");
            EnablePrivilege(processToken, "SeAssignPrimaryTokenPrivilege");
            EnablePrivilege(processToken, "SeIncreaseQuotaPrivilege");

            if (!DuplicateTokenEx(processToken, TOKEN_ALL_ACCESS, IntPtr.Zero,
                    SECURITY_IMPERSONATION, TOKEN_PRIMARY, out primary))
            {
                GuardLog.Warn($"session launch: DuplicateTokenEx failed err={Marshal.GetLastWin32Error()}");
                return null;
            }

            // Relocate the duplicated token into the interactive session.
            if (!SetTokenInformation(primary, TOKEN_SESSION_ID, ref sessionId, sizeof(uint)))
            {
                GuardLog.Warn($"session launch: SetTokenInformation(session={sessionId}) failed err={Marshal.GetLastWin32Error()}");
                return null;
            }

            var startupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = (int)STARTF_USESHOWWINDOW,
                wShowWindow = SW_HIDE // hides powershell's console; windows show themselves
            };
            var commandLine = new StringBuilder($"\"{exePath}\" {arguments}");
            if (!CreateProcessAsUser(primary, null, commandLine, IntPtr.Zero, IntPtr.Zero,
                    false, 0, IntPtr.Zero, null, ref startupInfo, out var info))
            {
                GuardLog.Warn($"session launch: CreateProcessAsUser failed err={Marshal.GetLastWin32Error()}");
                return null;
            }

            CloseHandle(info.hThread);
            var process = Process.GetProcessById((int)info.dwProcessId);
            CloseHandle(info.hProcess);
            GuardLog.Info($"session launch: pid={info.dwProcessId} session={sessionId}");
            return process;
        }
        catch (Exception ex)
        {
            GuardLog.Warn($"session launch: exception {ex.Message}");
            return null;
        }
        finally
        {
            if (primary != IntPtr.Zero) CloseHandle(primary);
            if (processToken != IntPtr.Zero) CloseHandle(processToken);
        }
    }

    private static void EnablePrivilege(IntPtr token, string name)
    {
        if (!LookupPrivilegeValue(null, name, out var luid)) return;
        var tp = new TOKEN_PRIVILEGES
        {
            PrivilegeCount = 1,
            Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
        };
        AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars;
        public int dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existingToken, uint desiredAccess,
        IntPtr tokenAttributes, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        ref uint tokenInformation, int tokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr hToken, string? lpApplicationName,
        StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, int dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);
}
