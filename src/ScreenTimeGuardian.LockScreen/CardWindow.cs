using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ScreenTimeGuardian.LockScreen;

// Pre-lock countdown card. Ported from the PowerShell/WinForms variant: that
// one was launched as a service child and its window never became visible on
// the user's desktop no matter which token/desktop combination was used,
// while this WPF window rides the exact LockScreen launch path
// (CreateProcessAsUser into the console session, pinned winsta0\default)
// that already renders the lock overlay reliably on this machine.
public class CardWindow : Window
{
    private readonly DateTime _startedAt = DateTime.Now;
    private readonly string _cancelFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ScreenTimeGuardian", "ipc", "STG_CARD_CANCEL");
    private readonly TextBlock _sub;
    private int _left;

    public CardWindow(int seconds, string title, string message)
    {
        _left = Math.Max(1, seconds);

        Title = "ScreenTime Guardian";
        Width = 440;
        Height = 130;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));

        var root = new StackPanel { Margin = new Thickness(12, 10, 12, 8) };
        root.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });
        _sub = new TextBlock
        {
            Text = SubText(message),
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };
        root.Children.Add(_sub);
        Content = root;

        // Top-right of the working area; WPF units are device-independent, so
        // the numbers are correct on scaled displays too.
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 20;
        Top = area.Top + 20;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            Topmost = true;
            if (Cancelled() || --_left <= 0)
            {
                timer.Stop();
                Close();
                return;
            }
            _sub.Text = SubText(message);
        };
        Loaded += (_, _) => timer.Start();
    }

    private string SubText(string message) => $"{message}  |  locks in {_left}s";

    // Same sentinel contract as the old card: a cancel written at or after
    // this card's start tears it down (parent unlocked, lock engaged).
    private bool Cancelled()
    {
        try
        {
            if (!File.Exists(_cancelFile)) return false;
            var parsed = DateTime.Parse(
                File.ReadAllText(_cancelFile).Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            if (parsed.Kind == DateTimeKind.Utc) parsed = parsed.ToLocalTime();
            return parsed >= _startedAt;
        }
        catch
        {
            return false;
        }
    }
}
