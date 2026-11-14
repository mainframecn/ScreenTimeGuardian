using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ScreenTimeGuardian.Shared;

// Shared diagnostics file. The service runs without a console and the lock screen
// runs full screen, so a test run has to be readable afterwards rather than watched
// live. Everything in here swallows its own failures: logging must never be the
// reason a lock or unlock fails.
public static class GuardLog
{
    private const long MaxLogBytes = 1_000_000;

    private static readonly object _gate = new();

    private static string _path = string.Empty;
    private static string _tag = "process";
    private static int _pid = 0;

    public static string LogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ScreenTimeGuardian", "logs");

    // Called once per process, before anything else logs.
    public static void Init(string tag)
    {
        try
        {
            var dir = LogDirectory();
            Directory.CreateDirectory(dir);

            _path = Path.Combine(dir, "guardian.log");
            _tag = tag;
            _pid = Environment.ProcessId;

            RotateIfNeeded();
            Write("start", $"process starting, log file {_path}");
        }
        catch
        {
            // No writable log directory: carry on, the app is still functional.
            _path = string.Empty;
        }
    }

    public static void Info(string message) => Write("info", message);

    public static void Warn(string message) => Write("warn", message);

    public static void Error(string message, Exception? exception = null)
    {
        if (exception == null)
        {
            Write("error", message);
            return;
        }

        Write("error", $"{message}: {exception.GetType().Name} {exception.Message}");
        if (exception.StackTrace != null)
        {
            Write("error", exception.StackTrace);
        }
    }

    private static void Write(string level, string message)
    {
        if (string.IsNullOrEmpty(_path))
        {
            return;
        }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{_pid}] [{level}] [{_tag}] {message}";

        try
        {
            lock (_gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics only: never propagate.
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            if (new FileInfo(_path).Length < MaxLogBytes)
            {
                return;
            }

            File.Replace(_path, $"{_path}.old", null);
        }
        catch
        {
            try
            {
                File.Delete(_path);
            }
            catch
            {
                // Nothing else to do.
            }
        }
    }

    public static string FilePath() => _path;
}
