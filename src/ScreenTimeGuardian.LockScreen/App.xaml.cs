using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using ScreenTimeGuardian.Shared;
using WinForms = System.Windows.Forms;

namespace ScreenTimeGuardian.LockScreen;

public partial class App : System.Windows.Application
{
    private InputBlocker? _inputBlocker;
    private readonly List<LockMainWindow> _windows = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        GuardLog.Init("lockscreen");

        int durationMinutes = 15;

        var args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--duration" && int.TryParse(args[i + 1], out var parsed))
            {
                durationMinutes = parsed;
            }
        }

        // Card mode: the service's pre-lock notification. No overlay, no input
        // blocking, no PIN handoff - one self-contained countdown window that
        // exits at zero or when the cancel sentinel lands.
        if (Array.IndexOf(args, "--card") >= 0)
        {
            var seconds = 60;
            var title = "ScreenTime Guardian";
            var message = string.Empty;
            for (int i = 1; i + 1 < args.Length; i++)
            {
                if (args[i] == "--seconds" && int.TryParse(args[i + 1], out var s)) seconds = s;
                else if (args[i] == "--title") title = args[i + 1];
                else if (args[i] == "--message") message = args[i + 1];
            }

            GuardLog.Info($"card mode seconds={seconds}");
            new CardWindow(seconds, title, message).Show();
            return;
        }

        // PIN handoff via IPC, never the command line: drop a request marker,
        // wait for the service's one-shot grant file. No PIN in process listings
        // or screenshots. Null PIN = unlock button disabled until it arrives.
        IpcMessages.SendPinRequest();
        var parentPin = IpcMessages.WaitForPinGrant(5000) ?? string.Empty;

        // The PIN value is never logged, only its length.
        GuardLog.Info($"startup duration={durationMinutes} pin length={parentPin.Length}");

        var screens = WinForms.Screen.AllScreens;
        GuardLog.Info($"displays detected: {screens.Length}");

        _inputBlocker = new InputBlocker();
        _inputBlocker.StartBlocking();

        foreach (var screen in screens)
        {
            var (dpiX, dpiY) = GetDisplayDpi(screen);
            var scaleX = Math.Max(dpiX, 1) / 96.0;
            var scaleY = Math.Max(dpiY, 1) / 96.0;
            var bounds = BoundsInWindowUnits(screen, scaleX, scaleY);
            var window = new LockMainWindow(durationMinutes, parentPin, screen.Primary, bounds);

            _windows.Add(window);
            window.Show();

            GuardLog.Info(string.Format(
                "{0} overlay screen={1} left={2:F0} top={3:F0} {4:F0}x{5:F0} scale={6:F2}x{7:F2}",
                screen.Primary ? "primary" : "secondary",
                screen.DeviceName,
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                scaleX, scaleY));
        }
    }

    // WinForms' Screen type has no DPI members at all, so the per-display
    // scale comes from the monitor API instead: MonitorFromRect locates the
    // monitor holding the screen's bounds and GetDpiForMonitor (shcore,
    // Windows 8.1+) reports its effective DPI. Fallbacks keep the overlay
    // placing itself rather than failing to start: system DPI, then 96.
    private static (uint DpiX, uint DpiY) GetDisplayDpi(WinForms.Screen screen)
    {
        var b = screen.Bounds;
        var rect = new RECT
        {
            left = b.X,
            top = b.Y,
            right = b.X + b.Width,
            bottom = b.Y + b.Height
        };

        var monitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero &&
            GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 &&
            dpiX > 0 && dpiY > 0)
        {
            return (dpiX, dpiY);
        }

        var system = GetDpiForSystem();
        return system > 0 ? (system, system) : (96u, 96u);
    }

    // WinForms reports pixel bounds; WPF measures in device-independent units
    // (96 per inch), so each display is divided by its own scale: a display
    // set to 150 percent scaling is not the same size in units as one at 100
    // percent.
    private static Rect BoundsInWindowUnits(WinForms.Screen screen, double scaleX, double scaleY)
    {
        var bounds = screen.Bounds;
        return new Rect(
            bounds.X / scaleX,
            bounds.Y / scaleY,
            bounds.Width / scaleX,
            bounds.Height / scaleY);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GuardLog.Info($"exiting, windows opened={_windows.Count}");
        _inputBlocker?.StopBlocking();
        base.OnExit(e);
    }

    // --- per-display DPI plumbing -----------------------------------------

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
