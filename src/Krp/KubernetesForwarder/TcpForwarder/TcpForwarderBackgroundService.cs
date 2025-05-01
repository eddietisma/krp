using Krp.KubernetesForwarder.PortForward;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.TcpForwarder;

public class TcpForwarderBackgroundService : BackgroundService
{
    private readonly PortForwardManager _portForwardManager;
    private readonly ILogger<TcpForwarderBackgroundService> _logger;
    private readonly TcpForwarderOptions _options;
    private TcpListener _listener;

    public TcpForwarderBackgroundService(PortForwardManager portForwardManager, ILogger<TcpForwarderBackgroundService> logger, IOptions<TcpForwarderOptions> options)
    {
        _portForwardManager = portForwardManager;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new TcpListener(_options.ListenAddress, _options.ListenPort);
        _listener.Start();
        _logger.LogInformation("Listening on {address}:{port}", _options.ListenAddress, _options.ListenPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync();
            _ = Task.Run(() => HandleConnectionAsync(client, stoppingToken), stoppingToken);
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

                    var portForwardHandler = _portForwardManager.GetByIpPort(localIp);
                    if (portForwardHandler == null)
                    {
                        _logger.LogWarning("Invalid url for proxy request: {localIp}", localIp);
                        var errorMessageBytes = GetErrorMessageBytes(localIp, localPort);
                        await client.GetStream().WriteAsync(errorMessageBytes, 0, errorMessageBytes.Length, stoppingToken);
                        return;
                    }

                    await portForwardHandler.EnsureRunningAsync();

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