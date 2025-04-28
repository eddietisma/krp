using Krp.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder;

public class PortForwardHandler : IDisposable
{
    private readonly ProcessRunner _processRunner;
    private readonly ILogger<PortForwardHandler> _logger;
    private Process _process;
    private int _localPort;
    private int? _localPortActual;

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
    public string Type { get; set; }
    public string Url => RemotePort == 80 ? $"{Resource}.{Namespace}" : $"{Resource}.{Namespace}:{RemotePort}";
    public string Hostname => $"{Resource}.{Namespace}";

    public PortForwardHandler(ProcessRunner processRunner, ILogger<PortForwardHandler> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task EnsureRunningAsync()
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

        var (process, logs) = await _processRunner.RunCommandAsync("kubectl", $"port-forward {Type}/{Resource} {LocalPort}:{RemotePort} -n {Namespace}");

        _process = process;

        foreach (var log in logs)
        {
            if (!ReverseStructuredLogging.TryParse("Forwarding from 127.0.0.1:{port}-> {targetPort}", log, out var values))
            {
                continue;
            }

            if (values.TryGetValue("port", out var value))
            {
                LocalPortActual = Convert.ToInt32(value);
            }
        }
    }

    public void Dispose()
    {
        if (_process != null)
        {
            _logger.LogInformation("Killing port-forwarding for {url}", Url);
        }

        _process?.Kill();
        _process?.Dispose();
    }
}