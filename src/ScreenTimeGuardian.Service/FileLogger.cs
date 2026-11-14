using System;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Shared;

namespace ScreenTimeGuardian.Service;

// Routes the ILogger calls already used across the service into the shared
// diagnostics file, so the timer, lock and registry decisions can be read back
// after a test run instead of needing a console attached.
internal sealed class FileLoggerProvider : ILoggerProvider
{
    public FileLoggerProvider()
    {
        GuardLog.Init("service");
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName);

    public void Dispose()
    {
    }
}

internal sealed class FileLogger : ILogger
{
    private const string CategorySeparator = ".";

    private readonly string _category;

    public FileLogger(string category)
    {
        // The generated category names are long namespaces; keep the last part.
        var lastDot = category.LastIndexOf(CategorySeparator, StringComparison.Ordinal);
        _category = lastDot < 0 ? category : category.Substring(lastDot + 1);
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = $"[{_category}] {formatter(state, exception)}";

        if (exception != null)
        {
            GuardLog.Error(message, exception);
            return;
        }

        switch (logLevel)
        {
            case LogLevel.Warning:
                GuardLog.Warn(message);
                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                GuardLog.Error(message);
                break;
            case LogLevel.Debug:
            case LogLevel.Trace:
                break;
            default:
                GuardLog.Info(message);
                break;
        }
    }
}
