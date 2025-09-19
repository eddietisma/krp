using Krp.Endpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Forwarders.TcpForwarder;

/// <summary>
/// Opens a TCP connection and forwards requests to Kubernetes based on originating ip (using domain-based IP per hostname in HOSTS file).
/// </summary>
public class TcpForwarder
{
    private readonly EndpointManager _endpointManager;
    private readonly ILogger<TcpForwarderBackgroundService> _logger;
    private readonly TcpForwarderOptions _options;
    private readonly List<TcpListener> _listeners = new();

    public TcpForwarder(EndpointManager endpointManager, ILogger<TcpForwarderBackgroundService> logger, IOptions<TcpForwarderOptions> options)
    {
        _endpointManager = endpointManager;
        _logger = logger;
        _options = options.Value;
    }

    public async Task Start(CancellationToken stoppingToken)
    {
        foreach (var port in _options.ListenPorts)
        {
            var listener = new TcpListener(_options.ListenAddress, port);
            listener.Start();
            _listeners.Add(listener);
            _logger.LogInformation("Listening on {address}:{port}", _options.ListenAddress, port);

            // Start accept loop for each port.
            _ = Task.Run(() => AcceptLoopAsync(listener, stoppingToken), stoppingToken);
        }

        await Task.CompletedTask;
    }


    public void Stop()
    {
        _logger.LogInformation("Stopping TCP forwarder");

        foreach (var listener in _listeners)
        {
            listener.Stop();
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = Task.Run(() => HandleConnectionAsync(client, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting connection");
            }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken stoppingToken)
    {
        try
        {
            using var tcpClient = client;
            using var target = new TcpClient();

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forwarding connection");
        }
    }

    private static byte[] GetErrorMessageBytes(IPAddress ip, int? port = 0)
    {
        var errorResponse =
            $"""
             HTTP/1.1 400 Bad Request
             Content-Type: application/json
             Connection: close

             "Invalid TCP proxy request. No matching routing for '{ip}:{port}'."
             """;
        return Encoding.UTF8.GetBytes(errorResponse);
    }
}