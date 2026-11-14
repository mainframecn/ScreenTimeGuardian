using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenTimeGuardian.Shared;
// UseWindowsForms is on for Screen.AllScreens; disambiguate the names it clashes with.
using Cursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace ScreenTimeGuardian.LockScreen;

public partial class LockMainWindow : Window
{
    private readonly DispatcherTimer _countdownTimer;
    private readonly string _parentPin;

    // Only the window on the primary display carries the PIN controls, so the
    // countdown and the unlock command live in one place.
    private readonly bool _interactive;

    private TimeSpan _remainingTime;
    private bool _isUnlocked;

    public LockMainWindow(int durationMinutes, string parentPin, bool interactive, Rect bounds)
    {
        InitializeComponent();

        _parentPin = parentPin;
        _interactive = interactive;
        _isUnlocked = false;
        _remainingTime = TimeSpan.FromMinutes(durationMinutes);

        // Placed per display instead of maximised: "maximised" means the whole
        // desktop, which on a multi monitor machine is one window spanning both
        // screens, and the previous resize call then squeezed it back down to the
        // primary display, clipping the unlock button off the bottom edge.
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;
        WindowState = WindowState.Normal;

        if (!_interactive)
        {
            // The other displays are covered so nothing can be reached on them:
            // hide the pointer and keep the PIN button off them, since only this
            // window can send the unlock command.
            Cursor = Cursors.None;
            ParentUnlockButton.Visibility = Visibility.Collapsed;
            PinPanel.IsEnabled = false;
        }

        UpdateCountdownDisplay();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += CountdownTimer_Tick;
        _countdownTimer.Start();

        Loaded += LockMainWindow_Loaded;
    }

    private void LockMainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);

        // Re-assert the stacking order without moving or resizing: the geometry
        // is already correct for this display.
        Win32.SetWindowPos(
            helper.Handle,
            Win32.HWND_TOPMOST,
            0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_SHOWWINDOW);

        if (_interactive)
        {
            Focus();
            Activate();
        }
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        if (_isUnlocked)
        {
            return;
        }

        _remainingTime = _remainingTime.Subtract(TimeSpan.FromSeconds(1));

        if (_remainingTime.TotalSeconds <= 0)
        {
            _countdownTimer.Stop();
            AutoUnlock();
            return;
        }

        UpdateCountdownDisplay();
    }

    private void UpdateCountdownDisplay()
    {
        var minutes = (int)_remainingTime.TotalMinutes;
        var seconds = _remainingTime.Seconds;
        CountdownText.Text = $"{minutes:D2}:{seconds:D2}";
    }

    private void AutoUnlock()
    {
        _isUnlocked = true;

        if (!_interactive)
        {
            return;
        }

        GuardLog.Info("break countdown finished");
        IpcMessages.SendCommand(IpcMessages.UNLOCK_SCREEN_CMD);
        MessageBox.Show("Break is over! You can use the computer again.", "ScreenTime Guardian",
            MessageBoxButton.OK, MessageBoxImage.Information);
        Application.Current?.Shutdown();
    }

    private void ParentUnlockButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!_interactive)
        {
            return;
        }

        GuardLog.Info("parent unlock button clicked");

        PinPanel.Visibility = PinPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (PinPanel.Visibility == Visibility.Visible)
        {
            PinInput.Focus();
        }
    }

    private void PinInput_PasswordChanged(object? sender, RoutedEventArgs e)
    {
        PinErrorText.Text = "";

        if (PinInput.Password.Length == 6)
        {
            if (PinInput.Password == _parentPin)
            {
                GuardLog.Info("parent PIN entered correctly");
                ParentUnlock();
            }
            else
            {
                GuardLog.Info("incorrect PIN entered");
                PinErrorText.Text = "Incorrect PIN";
                PinInput.Password = "";
                PinInput.Focus();
            }
        }
    }

    private void PinUnlock_Click(object? sender, RoutedEventArgs e)
    {
        if (PinInput.Password == _parentPin)
        {
            GuardLog.Info("parent unlock via Unlock button");
            ParentUnlock();
        }
        else
        {
            GuardLog.Info("incorrect PIN on Unlock button");
            PinErrorText.Text = "Incorrect PIN";
            PinInput.Password = "";
            PinInput.Focus();
        }
    }

    private void ParentUnlock()
    {
        _countdownTimer.Stop();
        _isUnlocked = true;
        GuardLog.Info("parent unlock requested");
        IpcMessages.SendCommand(IpcMessages.UNLOCK_SCREEN_CMD);
        Application.Current?.Shutdown();
    }

    private void PinCancel_Click(object? sender, RoutedEventArgs e)
    {
        GuardLog.Info("PIN entry cancelled");
        PinPanel.Visibility = Visibility.Collapsed;
        PinInput.Password = "";
        PinErrorText.Text = "";
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // A blank overlay must be closable, otherwise shutting the application
        // down on a multi monitor machine is blocked by the second window.
        if (!_isUnlocked && _interactive)
        {
            e.Cancel = true;
        }
    }
}
