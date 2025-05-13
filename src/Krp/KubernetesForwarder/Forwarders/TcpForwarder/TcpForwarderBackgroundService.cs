using Krp.KubernetesForwarder.Endpoints;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.Forwarders.TcpForwarder;

/// <summary>
/// Opens a TCP connection and forwards requests to Kubernetes based on originating ip (using domain-based IP per hostname in HOSTS file).
/// </summary>
public class TcpForwarderBackgroundService : BackgroundService
{
    private readonly EndpointManager _endpointManager;
    private readonly ILogger<TcpForwarderBackgroundService> _logger;
    private readonly TcpForwarderOptions _options;
    private TcpListener _listener;

    public TcpForwarderBackgroundService(EndpointManager endpointManager, ILogger<TcpForwarderBackgroundService> logger, IOptions<TcpForwarderOptions> options)
    {
        _endpointManager = endpointManager;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var port in _options.ListenPorts)
        {
            _listener = new TcpListener(_options.ListenAddress, port);
            _listener.Start();
            _logger.LogInformation("Listening on {address}:{port}", _options.ListenAddress, port);

            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                _ = Task.Run(() => HandleConnectionAsync(client, stoppingToken), stoppingToken);
            }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken stoppingToken)
    {
        try
        {
            using (client)
            {
                using (var target = new TcpClient())
                {
                    var localEndPoint = client.Client.LocalEndPoint as IPEndPoint;
                    var localIp = localEndPoint?.Address;
                    var localPort = localEndPoint?.Port;

                    _logger.LogInformation("Received request from {ip}:{port}", localIp, localPort);

                    var portForwardHandler = _endpointManager.GetHandlerByIpPort(localIp);
                    if (portForwardHandler == null)
                    {
                        _logger.LogWarning("Invalid url for proxy request: {localIp}", localIp);
                        var errorMessageBytes = GetErrorMessageBytes(localIp, localPort);
                        await client.GetStream().WriteAsync(errorMessageBytes, 0, errorMessageBytes.Length, stoppingToken);
                        return;
                    }

                    await portForwardHandler.EnsureRunningAsync();

                    // Setup live TCP connection between client and downstream port to let data flow.
                    await target.ConnectAsync(IPAddress.Loopback, portForwardHandler.LocalPort, stoppingToken);
                    var clientStream = client.GetStream();
                    var targetStream = target.GetStream();
                    var clientToTarget = clientStream.CopyToAsync(targetStream, stoppingToken);
                    var targetToClient = targetStream.CopyToAsync(clientStream, stoppingToken);
                    await Task.WhenAny(clientToTarget, targetToClient);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding connection");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping TCP forwarder");
        _listener.Stop();
        await base.StopAsync(cancellationToken);
    }

    public byte[] GetErrorMessageBytes(IPAddress ip, int? port = 0)
    {
        var errorResponse = $"""
HTTP/1.1 400 Bad Request
Content-Type: application/json
Connection: close

"Invalid TCP proxy request. No matching routing for '{ip}:{port}'."
""";
        return System.Text.Encoding.UTF8.GetBytes(errorResponse);
    }
}