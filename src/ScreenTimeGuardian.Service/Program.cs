using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Service;
using ScreenTimeGuardian.Shared;

GuardLog.Init("service");
GuardLog.Info($"service main starting; args={args.Length}");

// A service that dies silently is undiagnosable from the Event Log alone (SCM
// logs an empty message for 1053). Route every unhandled failure to GuardLog.
AppDomain.CurrentDomain.UnhandledException += (obj, ex) =>
{
    try { GuardLog.Error("unhandled exception", ex.ExceptionObject as Exception); } catch { }
};

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        // Console output is invisible for a Windows service, and the default
        // console logger is dropped when running as one. This provider writes
        // every log call to %PROGRAMDATA%\ScreenTimeGuardian\logs\guardian.log.
        logging.AddProvider(new FileLoggerProvider());
    })
    .UseWindowsService(options =>
    {
        options.ServiceName = "ScreenTimeGuardian";
    })
    .ConfigureServices(services =>
    {
        services.AddHostedService<Worker>();
    })
    .Build();

// Bisect marker: if this appears but Worker's first line does not, the hang
// is inside RunAsync's service wiring rather than in our code.
GuardLog.Info("host built; entering RunAsync");

await host.RunAsync();

GuardLog.Info("RunAsync returned");

