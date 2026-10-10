using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using static Krp.Dns.ConsoleControlHandlerNative;

namespace Krp.Dns;

public class DnsCleanupHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IDnsHandler _dnsHandler;
    private readonly ILogger<DnsCleanupHostedService> _logger;
    private Handler? _handler;
    private ConsoleCancelEventHandler? _cancelKeyPressHandler;
    private EventHandler? _processExitHandler;
    private PosixSignalRegistration? _sigintRegistration;
    private PosixSignalRegistration? _sigtermRegistration;
    private PosixSignalRegistration? _sighupRegistration;
    private int _shutdownRequested;

    public DnsCleanupHostedService(IHostApplicationLifetime applicationLifetime, IDnsHandler dnsHandler, ILogger<DnsCleanupHostedService> logger)
    {
        _applicationLifetime = applicationLifetime;
        _dnsHandler = dnsHandler;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            _handler = HandleConsoleEvent;
            Register(_handler);
            return Task.CompletedTask;
        }

        _cancelKeyPressHandler = (_, args) =>
        {
            args.Cancel = true;
            RequestShutdown("Ctrl+C");
        };
        Console.CancelKeyPress += _cancelKeyPressHandler;

        _processExitHandler = (_, _) => RequestShutdown("ProcessExit");
        AppDomain.CurrentDomain.ProcessExit += _processExitHandler;

        _sigintRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, _ => RequestShutdown("SIGINT"));
        _sigtermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => RequestShutdown("SIGTERM"));
        _sighupRegistration = PosixSignalRegistration.Create(PosixSignal.SIGHUP, _ => RequestShutdown("SIGHUP"));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows() && _handler != null)
        {
            Unregister(_handler);
        }
        else
        {
            if (_cancelKeyPressHandler != null)
            {
                Console.CancelKeyPress -= _cancelKeyPressHandler;
            }

            if (_processExitHandler != null)
            {
                AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
            }

            _sigintRegistration?.Dispose();
            _sigtermRegistration?.Dispose();
            _sighupRegistration?.Dispose();
        }

        return Task.CompletedTask;
    }

    private bool HandleConsoleEvent(CtrlType ctrlType)
    {
        switch (ctrlType)
        {
            case CtrlType.CtrlC:
            case CtrlType.CtrlBreak:
            case CtrlType.Close:
            case CtrlType.Logoff:
            case CtrlType.Shutdown:
                RequestShutdown($"Console:{ctrlType}");
                return true;
            default:
                return false;
        }
    }

    private void RequestShutdown(string reason)
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) == 1)
        {
            return;
        }

        _logger.LogInformation("Shutdown requested ({Reason}); stopping application", reason);

        try
        {
            var stopTask = _dnsHandler.StopAsync(CancellationToken.None);
            stopTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop DNS handler during shutdown");
        }

        _applicationLifetime.StopApplication();
    }
}

internal static class ConsoleControlHandlerNative
{
    public enum CtrlType
    {
        CtrlC = 0,
        CtrlBreak = 1,
        Close = 2,
        Logoff = 5,
        Shutdown = 6,
    }

    public delegate bool Handler(CtrlType ctrlType);

    [SupportedOSPlatform("windows")]
    public static bool Register(Handler handler) => SetConsoleCtrlHandler(handler, true);

    [SupportedOSPlatform("windows")]
    public static bool Unregister(Handler handler) => SetConsoleCtrlHandler(handler, false);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
}
