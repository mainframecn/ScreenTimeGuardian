# ScreenTime Guardian

A Windows parental control application that forces kids to take breaks from computer use.

## Features

- **Duration-based timer** - Allow X minutes of use, then force Y minute break
- **Full screen lockdown** - Custom lock screen that blocks all input
- **6-digit PIN** - Parent can unlock early with PIN
- **Pre-break notifications** - Windows Toast warnings at 5 min and 1 min
- **Anti-bypass** - Blocks Task Manager, Ctrl+Alt+Del, Alt+F4, Win key, shutdown menu
- **Service-based** - Runs as Windows service, starts at boot

## Requirements

- Windows 10 or 11
- .NET 8.0 Runtime
- Administrator privileges for installation

## Installation

1. Download and install [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) if not already installed
2. Right-click `installer\install.bat` and select "Run as administrator"
3. Run `ScreenTimeGuardian.Config.exe` to set your parent PIN and usage duration

## Configuration

Open `ScreenTimeGuardian.Config.exe` to configure:

| Setting | Default | Description |
|---------|---------|-------------|
| Usage Duration | 30 min | How long the child can use the computer |
| Break Duration | 15 min | How long the computer is locked |
| Parent PIN | 123456 | 6-digit PIN to unlock early |
| Notifications | Enabled | Show Toast warnings before lockout |
| Warning Time | 5 min | When to show the warning notification |

## Uninstallation

Right-click `installer\uninstall.bat` and select "Run as administrator"

## Project Structure

```
ScreenTimeGuardian/
├── src/
│   ├── ScreenTimeGuardian.Shared/       # Shared models and Win32 API
│   ├── ScreenTimeGuardian.Service/      # Windows Service (background)
│   ├── ScreenTimeGuardian.LockScreen/   # Fullscreen lock overlay
│   └── ScreenTimeGuardian.Config/       # Parent configuration UI
├── installer/
│   ├── install.bat                      # Installation script
│   ├── uninstall.bat                    # Uninstallation script
│   └── dev-run.cmd                      # Publish + run the service in a console
└── README.md
```

## How It Works

1. **Service** starts at boot and counts elapsed usage time (idle time is not
   subtracted yet: the activity detector is written but not wired in, so time
   spent away from the keyboard still counts toward the limit)
2. After configured duration, Toast notification warns child
3. At time limit, fullscreen lock overlay appears
4. Child cannot use computer during break period
5. Parent can enter 6-digit PIN to unlock early
6. After break duration, computer unlocks automatically

## Security Features

- Service runs as LocalSystem (highest privilege)
- Task Manager and the shutdown menu are disabled only **while a break is active**, via the machine-level policy (`HKLM`), and restored when the break ends or the service stops
- A SYSTEM-account scheduled task re-starts the service every minute if it is not running
- Keyboard hooks block escape combinations while the lock screen is up
- Leftover policy from an interrupted break is cleared at service startup

**Limits worth knowing.** A Windows service that runs an ordinary executable can always be stopped by an account with administrator rights on that machine; only kernel-mode drivers are immune, and this tool is not one. Recovery actions fire only on a crash, not on a deliberate stop - which is why the watchdog task exists. Anyone on that PC who can open an elevated command prompt can undo every software measure here, including deleting the watchdog.

## Field Test Checklist

Run this on one child's machine, not the development machine. The whole point is
to find out what a service running as LocalSystem is actually allowed to do to
the signed-in child's desktop.

1. Install, sign the child in, and set `UsageDurationMinutes` low (2-3 minutes)
   through the Config app so a break starts quickly.
2. Watch the desktop when the break begins. Does the lock window appear over the
   child's desktop, and does the countdown run down?
3. Read the service log: Event Viewer, Application log, source
   `ScreenTimeGuardian`. A line saying the lock screen "exited immediately with
   code N" means the window never appeared, which points at Windows session
   isolation rather than a bug in the timer.
4. A/B the same build in a console: stop the service, then run
   `installer\dev-run.cmd`, which publishes all three programs into one folder
   and starts the service in that console while you watch it. If the window
   appears there but not under the service, session isolation is confirmed and
   the lock screen has to be launched into the child's own session instead.
5. While a break is running, check Task Manager and the shutdown menu are gone,
   and that they come back when the break ends.

Do not leave a machine sitting on step 5 unattended: the registry values that
hide those dialogs are machine-wide for the duration of a break.

## Troubleshooting

**Service won't start:**
- Ensure you ran install.bat as Administrator
- Check Windows Event Viewer for errors

**Task Manager still accessible:**
- Protection applies only while a break is active, so check during a lockout
- Check registry: `HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\System\DisableTaskMgr` should be 1
- Both hives are read by Task Manager, but a service running as LocalSystem cannot see the signed-in user's HKCU, so the machine-level value is the one that works

**Notifications not appearing:**
- Ensure notifications are enabled in Config app
- Check Windows notification settings

## License

MIT
