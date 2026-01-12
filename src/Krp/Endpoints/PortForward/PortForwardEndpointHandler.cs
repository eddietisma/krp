using Krp.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Endpoints.PortForward;

public class PortForwardEndpointHandler : IEndpointHandler
{
    private readonly ProcessRunner _processRunner;
    private readonly ILogger<PortForwardEndpointHandler> _logger;
    private Process _process;
    private int _localPort;
    private int? _localPortActual;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    /// <summary>
    /// The local port to use for the port-forwarding. If set to 0, a random port will be used.
    /// Returns the actual port used for the port-forwarding if LocalPortActual is not null.
    /// </summary>
    public int LocalPort
    {
        get => _localPort == 0 && _localPortActual != null && _process is { HasExited: false } ? _localPortActual.Value : _localPort;
        set => _localPort = value;
    }

    public int? LocalPortActual
    {
        get => _localPortActual;
        set => _localPortActual = value;
    }

    public bool IsStatic { get; set; }
    public string Namespace { get; set; }
    public int RemotePort { get; set; }
    public string Resource { get; set; }

    public string Url
    {
        get => RemotePort == 80 ? Host : $"{Host}:{RemotePort}";
        set => throw new NotImplementedException();
    }

    public string Path { get; set; }

    public string Host
    {
        get => $"{Resource.Substring("service/".Length)}.{Namespace}";
        set => throw new NotImplementedException();
    }
    public bool IsActive
    {
        get => _process is { HasExited: false };
    }

    public IPAddress LocalIp { get; set; }

    public PortForwardEndpointHandler(ProcessRunner processRunner, ILogger<PortForwardEndpointHandler> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task EnsureRunningAsync()
    {
        // Prevent running same kubectl command simultaneously.
        await _lock.WaitAsync();

        try
        {
            if (_process is { HasExited: false })
            {
                return;
            }

            if (LocalPort != 0 && !PortChecker.TryIsPortAvailable(LocalPort))
            {
                _logger.LogError("Port-forward failed, port {port} is not available", LocalPort);
                return;
            }

            var (process, logs) = await _processRunner.RunCommandAsync("kubectl", "port-forward", Resource, $"{LocalPort}:{RemotePort}", "-n", Namespace);

            _process = process;

            foreach (var log in logs)
            {
                if (!ReverseStructuredLogging.TryParse("Forwarding from 127.0.0.1:{port} -> {targetPort}", log, out var values))
                {
                    continue;
                }

                if (values.TryGetValue("port", out var value))
                {
                    LocalPortActual = Convert.ToInt32(value);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public string GetDestinationUrl()
    {
        return $"http://localhost:{LocalPort}";
    }

    public void Dispose()
    {
        if (_process != null)
        {
            _logger.LogInformation("Killing port-forwarding for {url}", Url);
        }

        try
        {
            _process?.Kill();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill port-forwarding process for {url}", Url);
        }

        _process?.Dispose();
        _process = null;
    }
}
