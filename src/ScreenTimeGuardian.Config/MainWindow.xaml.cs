using System.Windows;
using ScreenTimeGuardian.Shared;

namespace ScreenTimeGuardian.Config;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Path in the title: any screenshot of this window identifies exactly
        // which config file is being read on this machine.
        Title = $"ScreenTime Guardian - settings ({AppConfig.GetConfigPath()})";
        LoadSettings();
    }

    private void LoadSettings()
    {
        var config = AppConfig.Load();
        UsageDurationBox.Text = config.UsageDurationMinutes.ToString();
        BreakDurationBox.Text = config.BreakDurationMinutes.ToString();
        PinBox.Password = config.ParentPin;
        NotificationsCheckBox.IsChecked = config.NotificationsEnabled;

        foreach (var item in WarningMinutesCombo.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem comboItem &&
                comboItem.Content.ToString() == config.NotificationWarningMinutes.ToString())
            {
                comboItem.IsSelected = true;
                break;
            }
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(UsageDurationBox.Text, out int usageMinutes) || usageMinutes < 1 || usageMinutes > 480)
        {
            ShowStatus("Usage duration must be between 1 and 480 minutes.", true);
            return;
        }

        if (!int.TryParse(BreakDurationBox.Text, out int breakMinutes) || breakMinutes < 1 || breakMinutes > 120)
        {
            ShowStatus("Break duration must be between 1 and 120 minutes.", true);
            return;
        }

        string pin = PinBox.Password;
        if (string.IsNullOrEmpty(pin) || pin.Length != 6 || !long.TryParse(pin, out _))
        {
            ShowStatus("PIN must be exactly 6 digits.", true);
            return;
        }

        var config = AppConfig.Load();
        config.UsageDurationMinutes = usageMinutes;
        config.BreakDurationMinutes = breakMinutes;
        config.ParentPin = pin;
        config.NotificationsEnabled = NotificationsCheckBox.IsChecked == true;

        if (WarningMinutesCombo.SelectedItem is System.Windows.Controls.ComboBoxItem selectedWarning &&
            int.TryParse(selectedWarning.Content.ToString(), out int warningMinutes))
        {
            config.NotificationWarningMinutes = warningMinutes;
        }

        // Save, with a fallback: a locked/held config.json used to kill the
        // window unhandled (no status, file untouched). Now: try a live write
        // first; if that fails, stop the service to release any handle, retry
        // the write, then start the service again - the restart also loads the
        // new config, so settings take effect either way.
        try
        {
            config.Save();
        }
        catch (Exception firstError)
        {
            ShowStatus($"Write blocked ({firstError.GetType().Name}) - stopping service, retrying...", false);
            ServiceControl.TryStop();

            try
            {
                config.Save();
            }
            catch (Exception ex)
            {
                ShowStatus($"Save FAILED even with the service stopped: {ex.Message}", true);
                return;
            }

            ServiceControl.Start();
            ShowStatus("Settings saved. Service restarted with the new settings.", false);
            return;
        }

        try
        {
            IpcMessages.SendCommand(IpcMessages.RELOAD_CONFIG_CMD);
            ShowStatus("Settings saved and applied.", false);
        }
        catch
        {
            ShowStatus("Saved, but the running service could not be notified - restart it to apply.", true);
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Reset all settings to defaults?", "Confirm Reset",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var config = new AppConfig();
            try
            {
                config.Save();
            }
            catch (Exception firstError)
            {
                // Same fallback as Save: a held file blocks the write, so stop
                // the service, retry, then start it again.
                ShowStatus($"Write blocked ({firstError.GetType().Name}) - stopping service, retrying...", false);
                ServiceControl.TryStop();

                try
                {
                    config.Save();
                }
                catch (Exception ex)
                {
                    ShowStatus($"Reset FAILED even with the service stopped: {ex.Message}", true);
                    return;
                }

                ServiceControl.Start();
                LoadSettings();
                ShowStatus("Settings reset to defaults. Service restarted with the new settings.", false);
                return;
            }

            try
            {
                IpcMessages.SendCommand(IpcMessages.RELOAD_CONFIG_CMD);
            }
            catch
            {
                ShowStatus("Reset, but the running service could not be notified - restart it to apply.", true);
                return;
            }

            LoadSettings();
            ShowStatus("Settings reset to defaults.", false);
        }
    }

    private void LockNowButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Lock the computer now?", "Confirm Lock",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            IpcMessages.SendCommand(IpcMessages.LOCK_SCREEN_CMD);
            ShowStatus("Lock command sent.", false);
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 68, 68))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 136));

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += (s, ev) =>
        {
            StatusText.Text = "";
            timer.Stop();
        };
        timer.Start();
    }
}
