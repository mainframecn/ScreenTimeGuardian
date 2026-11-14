using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Shared;

namespace ScreenTimeGuardian.Service;

public class ActivityMonitor
{
    private readonly ILogger<ActivityMonitor>? _logger;
    private DateTime _lastActivityTime;
    private bool _isIdle;
    private readonly uint _idleThresholdSeconds;

    public event Action? OnBecameActive;
    public event Action? OnBecameIdle;
    public bool IsActive => !_isIdle;

    public ActivityMonitor(uint idleThresholdSeconds = 60, ILogger<ActivityMonitor>? logger = null)
    {
        _idleThresholdSeconds = idleThresholdSeconds;
        _logger = logger;
        _lastActivityTime = DateTime.UtcNow;
        _isIdle = false;
    }

    public void UpdateActivity()
    {
        _lastActivityTime = DateTime.UtcNow;
        if (_isIdle)
        {
            _isIdle = false;
            _logger?.LogInformation("User became active.");
            OnBecameActive?.Invoke();
        }
    }

    public void CheckIdleStatus()
    {
        var idleSeconds = (uint)(DateTime.UtcNow - _lastActivityTime).TotalSeconds;
        if (!_isIdle && idleSeconds >= _idleThresholdSeconds)
        {
            _isIdle = true;
            _logger?.LogInformation("User became idle after {Seconds}s.", idleSeconds);
            OnBecameIdle?.Invoke();
        }
    }

    public TimeSpan GetIdleTime()
    {
        return DateTime.UtcNow - _lastActivityTime;
    }

    public static uint GetLastInputTick()
    {
        var lastInputInfo = new Win32.LASTINPUTINFO();
        lastInputInfo.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win32.LASTINPUTINFO));
        if (Win32.GetLastInputInfo(ref lastInputInfo))
        {
            return lastInputInfo.dwTime;
        }
        return 0;
    }

    public static TimeSpan GetSystemIdleTime()
    {
        uint lastInputTick = GetLastInputTick();
        uint currentTick = Win32.GetTickCount();
        uint idleMs = currentTick - lastInputTick;
        return TimeSpan.FromMilliseconds(idleMs);
    }
}
