using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenTimeGuardian.Service;
using ScreenTimeGuardian.Shared;

GuardLog.Init("service");
GuardLog.Info($"service main starting; args={args.Length}");

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

await host.RunAsync();

