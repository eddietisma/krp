using Krp.KubernetesForwarder.PortForward;
using Krp.KubernetesForwarder.TcpForwarder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.TcpWithHttpForwarder;

/// <summary>
/// Opens a TCP connection and inspects traffic and routes HTTP requests to different HTTP forwarding ports (81 for HTTP/1.1 and 82 for HTTP/2).
/// </summary>
public class TcpWithHttpForwarderBackgroundService : BackgroundService
{
    private static readonly byte[] _http2Preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
    private readonly PortForwardManager _portForwardManager;
    private readonly ILogger<TcpWithHttpForwarderBackgroundService> _logger;
    private readonly TcpForwarderOptions _options;
    private TcpListener _listener;

    public TcpWithHttpForwarderBackgroundService(PortForwardManager portForwardManager, ILogger<TcpWithHttpForwarderBackgroundService> logger, IOptions<TcpForwarderOptions> options)
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

                    if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
                    {
                        var clientStream = client.GetStream();

                        var buffer = new byte[24];
                        var bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);

                        var isHttp2 = IsHttp2Preface(buffer, bytesRead);
                        var targetPort = isHttp2 ? 82 : 81;


                        await target.ConnectAsync(IPAddress.Loopback, targetPort, stoppingToken);

                        var targetStream = target.GetStream();
                        await targetStream.WriteAsync(buffer, 0, bytesRead, stoppingToken);
                        var clientToTarget = clientStream.CopyToAsync(targetStream, stoppingToken);
                        var targetToClient = targetStream.CopyToAsync(clientStream, stoppingToken);

                        await Task.WhenAny(clientToTarget, targetToClient);
                    }
                    else
                    {
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

    /// <summary>
    /// Detects the HTTP/2 clear-text preface (24 bytes) in the initial client payload.
    /// This lets us route h2c vs. HTTP/1.1 traffic to different Kestrel ports.
    /// </summary>
    private bool IsHttp2Preface(byte[] buffer, int bytesRead)
    {
        // Kestrel only support multiple protocols over HTTPS since the protocol selection happens during ALPN protocol negotiation.
        // We can however detect the HTTP/2 preface and use that to determine if the client is using HTTP/2 or HTTP/1.1 and route to
        // a specific backend by hosting HTTP/2 and HTTP/1 on different Kestrel ports.
        // https://httpwg.org/specs/rfc7540.html#ConnectionHeader
        // https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore?view=aspnetcore-9.0&tabs=visual-studio#protocol-negotiation
        // https://github.com/dotnet/aspnetcore/issues/13502

        return bytesRead >= 24 && buffer.Take(24).SequenceEqual(_http2Preface);
    }
}