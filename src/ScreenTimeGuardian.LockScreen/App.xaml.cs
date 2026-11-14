using System;
using System.Collections.Generic;
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
        string parentPin = "123456";

        var args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--duration" && int.TryParse(args[i + 1], out var parsed))
            {
                durationMinutes = parsed;
            }
            else if (args[i] == "--pin")
            {
                parentPin = args[i + 1];
            }
        }

        // The PIN arrives on the command line, so it is never written to the log.
        GuardLog.Info($"startup duration={durationMinutes} pin length={parentPin.Length}");

        var screens = WinForms.Screen.AllScreens;
        GuardLog.Info($"displays detected: {screens.Length}");

        _inputBlocker = new InputBlocker();
        _inputBlocker.StartBlocking();

        foreach (var screen in screens)
        {
            var bounds = BoundsInWindowUnits(screen);
            // Per-display scale factors for the log line below; derived inside
            // BoundsInWindowUnits, so recompute cheaply here rather than widen
            // its signature.
            var scaleX = Math.Max(GetDisplayDpi(screen), 1) / 96.0;
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

    // WinForms reports pixel bounds plus each display's own DPI. WPF measures in
    // device-independent units (96 per inch), so each display has to be divided
    // by its own scale: a display set to 150 percent scaling is not the same
    // size in units as one at 100 percent. DpiX/DpiY are instance properties
    // on Screen, so they are read from the screen object itself.
    private static Rect BoundsInWindowUnits(WinForms.Screen screen)
    {
        var scaleX = Math.Max(screen.DpiX, 1) / 96.0;
        var scaleY = Math.Max(screen.DpiY, 1) / 96.0;
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
}
