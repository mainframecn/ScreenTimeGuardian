using System.IO;
using System.Text.Json;

namespace ScreenTimeGuardian.Shared;

public static class IpcMessages
{
    public const string LOCK_SCREEN_CMD = "STG_LOCK";
    public const string UNLOCK_SCREEN_CMD = "STG_UNLOCK";
    public const string STATUS_REQUEST = "STG_STATUS";
    public const string STATUS_RESPONSE = "STG_STATUS_OK";

    // PIN handoff: the overlay must never receive the parent PIN on its
    // command line (visible in process listings). It drops a request marker;
    // the service — sole owner of config.json — replies with a one-shot
    // grant file the overlay polls, reads and deletes.
    public const string PIN_REQUEST = "STG_PINREQ";
    public const string PIN_GRANT = "STG_PINGrant";

    private static readonly string IpcDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ScreenTimeGuardian", "ipc");

    // Names are "<COMMAND>_<yyyyMMddHHmmssfffffff>.txt". The timestamp suffix is
    // what orders them; two commands sent in the same second differ at tick level.
    // LastIndexOf, not IndexOf: the key must start after the COMMAND, and the
    // name itself contains an underscore ("STG_LOCK").
    private static string TimestampOf(string fileName)
    {
        var separator = fileName.LastIndexOf('_');
        return separator < 0 ? fileName : fileName.Substring(separator + 1);
    }

    // The commands the service knows how to act on. STATUS_* are not dispatched.
    private static readonly string[] DispatchedCommands = [LOCK_SCREEN_CMD, UNLOCK_SCREEN_CMD, PIN_REQUEST];

    public static void SendCommand(string command)
    {
        Directory.CreateDirectory(IpcDir);
        // Sub-second precision keeps same-second commands distinct and keeps
        // the names sortable oldest-first.
        var cmdPath = Path.Combine(IpcDir, $"{command}_{DateTime.UtcNow:yyyyMMddHHmmssfffffff}.txt");
        File.WriteAllText(cmdPath, $"{command} {DateTime.UtcNow.ToString("o")}");
    }

    public static void SendPinRequest() => SendCommand(PIN_REQUEST);

    // One-shot PIN fetch: poll for the grant file, read it, delete it. The
    // service writes it in response to STG_PINREQ; bounded wait so a missing
    // service does not hang the overlay forever (it falls back to no PIN).
    public static string? WaitForPinGrant(int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(IpcDir))
            {
                var grant = Directory
                    .GetFiles(IpcDir, $"{PIN_GRANT}_*.txt")
                    .OrderBy(f => TimestampOf(Path.GetFileName(f)), StringComparer.Ordinal)
                    .FirstOrDefault();
                if (grant != null)
                {
                    try
                    {
                        var pin = File.ReadAllText(grant).Trim();
                        return pin.Length > 0 ? pin : null;
                    }
                    finally
                    {
                        try { File.Delete(grant); } catch { }
                    }
                }
            }
            Thread.Sleep(100);
        }
        return null;
    }

    public static string? WaitForCommand(int timeoutMs = 1000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(IpcDir))
            {
                // Order by the timestamp suffix, not the whole name: sorting by name
                // would group every STG_LOCK ahead of every STG_UNLOCK and replay
                // them out of the order they were sent.
                var files = Directory
                    .GetFiles(IpcDir, "STG_*.txt")
                    .OrderBy(f => TimestampOf(Path.GetFileName(f)), StringComparer.Ordinal)
                    .ToArray();

                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    string? matched = null;

                    foreach (var command in DispatchedCommands)
                    {
                        if (fileName.StartsWith($"{command}_", StringComparison.Ordinal))
                        {
                            matched = command;
                            break;
                        }
                    }

                    // Consume the file whether or not we act on it, otherwise
                    // an unrecognised marker is re-read on every tick forever.
                    try { File.Delete(file); } catch { }

                    if (matched != null) return matched;
                }
            }

            Thread.Sleep(100);
        }
        return null;
    }

    public static void CleanupCommands()
    {
        if (Directory.Exists(IpcDir))
        {
            foreach (var file in Directory.GetFiles(IpcDir, "*.txt"))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }
}
