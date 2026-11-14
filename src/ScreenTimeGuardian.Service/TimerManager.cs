using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Shared;

namespace ScreenTimeGuardian.Service;

public class TimerManager
{
    private readonly ILogger? _logger;
    private System.Threading.Timer? _usageTimer;
    private System.Threading.Timer? _breakTimer;
    private System.Threading.Timer? _activityCheckTimer;

    private AppConfig _config = new();
    private DateTime _usageStartTime;
    private DateTime? _breakStartTime;
    private bool _isOnBreak;
    private bool _warning5Sent;
    private bool _warning1Sent;
    private TimeSpan _lastUsageElapsed = TimeSpan.Zero;
    private TimeSpan _remainingUsage;
    private TimeSpan _remainingBreak;

    public event Action? OnUsageTimeReached;
    public event Action? OnBreakTimeReached;
    public event Action<int, int>? OnNotificationWarning; // (minutesLabel, actualSecondsLeft)

    public bool IsOnBreak => _isOnBreak;
    public TimeSpan RemainingUsage => _remainingUsage;
    public TimeSpan RemainingBreak => _remainingBreak;

    private readonly ActivityMonitor _activityMonitor;

    // Long-absence exclusion: idle beyond the threshold stops counting.
    // Per-second idle subtraction would hand out free time for movie/gamepad
    // sessions (XInput does not refresh LASTINPUTINFO), so it is deliberately
    // NOT done. Toggled via Start(excludeIdleTime:) and ResetTimers().
    private bool _excludeIdleTime = true;

    public TimerManager(ILogger? logger = null)
    {
        _logger = logger;
        _activityMonitor = new ActivityMonitor(idleThresholdSeconds: 300);
    }

    public void Start(AppConfig config, bool excludeIdleTime = true)
    {
        _config = config;
        _excludeIdleTime = excludeIdleTime;
        _usageStartTime = DateTime.UtcNow;
        _isOnBreak = false;
        _warning5Sent = false;
        _warning1Sent = false;

        // Event-driven activity is dead: a session-0 service receives no input
        // events, so UpdateActivity() never fires and OnBecameIdle would freeze
        // the clock forever. Idle detection uses GetSystemIdleTime() (LASTINPUTINFO)
        // polled in OnUsageTick instead; the event plumbing stays for future
        // user-session channels but drives nothing here.

        _usageTimer = new System.Threading.Timer(OnUsageTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        _activityCheckTimer = new System.Threading.Timer(_ =>
        {
            _activityMonitor.CheckIdleStatus();
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        _logger?.LogInformation("TimerManager started. Usage limit: {Minutes} min", config.UsageDurationMinutes);
    }

    public void Stop()
    {
        _usageTimer?.Dispose();
        _breakTimer?.Dispose();
        _activityCheckTimer?.Dispose();
    }

    public void ResetTimers(bool excludeIdleTime = true)
    {
        _logger?.LogInformation("Timers reset by parent override.");
        Stop();
        Start(_config, excludeIdleTime);
    }

    private void OnUsageTick(object? state)
    {
        if (_isOnBreak) return;

        // Idle exclusion: absent user -> clock frozen, no time charged.
        // LASTINPUTINFO is system-wide, so this freezes whenever NOBODY at the
        // desk typed or moved the mouse for the full threshold (300 s).
        if (_excludeIdleTime && ActivityMonitor.GetSystemIdleTime().TotalSeconds >= 300)
        {
            _usageStartTime = DateTime.UtcNow - _lastUsageElapsed;
            return;
        }

        var elapsed = DateTime.UtcNow - _usageStartTime;
        _lastUsageElapsed = elapsed;
        var totalTime = TimeSpan.FromMinutes(_config.UsageDurationMinutes);
        _remainingUsage = totalTime - elapsed;

        if (_remainingUsage.TotalSeconds <= 0)
        {
            _logger?.LogInformation("Usage time expired. Starting break.");
            StartBreak();
            return;
        }

        if (_config.NotificationsEnabled)
        {
            var remainingMinutes = (int)Math.Ceiling(_remainingUsage.TotalMinutes);

            if (!_warning5Sent && remainingMinutes <= _config.NotificationWarningMinutes && remainingMinutes > _config.NotificationUrgentMinutes)
            {
                _warning5Sent = true;
                // The card's live countdown must match the real lock time, not a
                // fixed display duration: pass the actual seconds left so "Break in
                // 5 minutes" cannot sit next to a "locks in 107s" counter.
                OnNotificationWarning?.Invoke(_config.NotificationWarningMinutes, (int)Math.Ceiling(_remainingUsage.TotalSeconds));
            }

            if (!_warning1Sent && remainingMinutes <= _config.NotificationUrgentMinutes)
            {
                _warning1Sent = true;
                OnNotificationWarning?.Invoke(_config.NotificationUrgentMinutes, (int)Math.Ceiling(_remainingUsage.TotalSeconds));
            }
        }
    }

    private void StartBreak()
    {
        _isOnBreak = true;
        _breakStartTime = DateTime.UtcNow;
        _warning5Sent = false;
        _warning1Sent = false;

        _usageTimer?.Dispose();

        _breakTimer = new System.Threading.Timer(OnBreakTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        OnUsageTimeReached?.Invoke();
    }

    private void OnBreakTick(object? state)
    {
        if (!_isOnBreak || _breakStartTime == null) return;

        var elapsed = DateTime.UtcNow - _breakStartTime.Value;
        var breakTime = TimeSpan.FromMinutes(_config.BreakDurationMinutes);
        _remainingBreak = breakTime - elapsed;

        if (_remainingBreak.TotalSeconds <= 0)
        {
            _logger?.LogInformation("Break time expired. Resuming usage.");
            EndBreak();
            return;
        }
    }

    private void EndBreak()
    {
        _isOnBreak = false;
        _breakStartTime = null;
        _usageStartTime = DateTime.UtcNow;

        _breakTimer?.Dispose();

        _usageTimer = new System.Threading.Timer(OnUsageTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        OnBreakTimeReached?.Invoke();
    }
}
