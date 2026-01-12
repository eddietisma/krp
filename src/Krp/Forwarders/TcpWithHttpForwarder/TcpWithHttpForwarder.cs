using Krp.Endpoints;
using Krp.Forwarders.HttpForwarder;
using Krp.Forwarders.TcpForwarder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Forwarders.TcpWithHttpForwarder;

/// <summary>
/// Opens a TCP connection and inspects traffic and routes HTTP requests to different HTTP forwarding ports (81 for HTTP/1.1 and 82 for HTTP/2).
/// </summary>
public class TcpWithHttpForwarder
{
    private static ReadOnlySpan<byte> Http2Preface => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;
    private readonly EndpointManager _endpointManager;
    private readonly ILogger<TcpWithHttpForwarderBackgroundService> _logger;
    private readonly TcpForwarderOptions _tcpOptions;
    private readonly HttpForwarderOptions _httpOptions;
    private readonly List<TcpListener> _listeners = new();

    public TcpWithHttpForwarder(
        EndpointManager endpointManager,
        ILogger<TcpWithHttpForwarderBackgroundService> logger,
        IOptions<TcpForwarderOptions> tcpOptions,
        IOptions<HttpForwarderOptions> httpOptions)
    {
        _endpointManager = endpointManager;
        _logger = logger;
        _httpOptions = httpOptions.Value;
        _tcpOptions = tcpOptions.Value;
    }

    public async Task Start(CancellationToken stoppingToken)
    {
        foreach (var port in _tcpOptions.ListenPorts)
        {
            var listener = new TcpListener(_tcpOptions.ListenAddress, port);
            listener.Start();
            _listeners.Add(listener);
            _logger.LogInformation("Listening on {address}:{port}", _tcpOptions.ListenAddress, port);

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

            var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            var remoteIp = remoteEndPoint?.Address;
            if (remoteIp == null || !IPAddress.IsLoopback(remoteIp))
            {
                _logger.LogWarning("Rejected non-loopback client {ip}", remoteIp);
                return;
            }

            var localEndPoint = client.Client.LocalEndPoint as IPEndPoint;
            var localIp = localEndPoint!.Address;
            var localPort = localEndPoint!.Port;

            _logger.LogInformation("Received request from {ip}:{port}", localIp, localPort);

            var clientStream = client.GetStream();

            var buffer = new byte[2048];
            var bytesRead = 0;

            var targetIp = localIp;
            var targetPort = 0;

            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            {
                var portForwardHandler = _endpointManager.GetHandlerByIpPort(localIp);
                if (portForwardHandler != null)
                {
                    await portForwardHandler.EnsureRunningAsync();

                    targetIp = IPAddress.Loopback;
                    targetPort = portForwardHandler.LocalPort;
                }
            }

            if (targetPort == 0)
            {
                switch (localPort)
                {
                    case 80:
                        // Handle HTTP/2 over cleartext (h2c) and HTTP/1.1.
                        bytesRead = await clientStream.ReadAsync(buffer, 0, 24, stoppingToken);
                        var isHttp2 = IsHttp2Preface(buffer.AsSpan(0, bytesRead));

                        // Forward to HttpForwarder for routing using HTTP headers.
                        targetPort = isHttp2 ? _httpOptions.Http2Port : _httpOptions.HttpPort;
                        break;
                    case 443:
                        // Forward to HttpForwarder for routing using HTTP headers.
                        targetPort = _httpOptions.HttpsPort;

                        //// TODO: Since TCP connections are stream based, once the tunnel is setup we can no longer react to when HTTP endpoints check switches state.
                        //// TODO: Idea - once a hostname has been setup, react to when ANY HTTP endpoint changes state and disconnect all active HTTP tunnels.
                        //if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
                        //{
                        //    // Fetch HOST from SNI since we can't use loopback IPs for routing.
                        //    bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                        //    var host = ParseSniHostname(buffer, bytesRead);
                        //    if (!string.IsNullOrEmpty(host))
                        //    {
                        //        var endpointHandler = _endpointManager.GetHandlerByHost(host);
                        //        if (endpointHandler.Any(x => !PortChecker.TryIsPortAvailable(x.LocalPort)))
                        //        {
                        //            // Send to HTTP forwarder if at least one endpoint has an active port for this host.
                        //            targetIp = IPAddress.Loopback;
                        //            targetPort = _httpOptions.HttpsPort;
                        //        }
                        //        else
                        //        {
                        //            // Bypass HTTP forwarder to prevent unnecessary overhead (e.g. TLS termination).
                        //            targetIp = await _dnsLookupHandler.QueryAsync(host);
                        //            targetPort = localPort;
                        //        }
                        //    }
                        //}
                        break;
                    default:
                        _logger.LogWarning("Invalid url for proxy request: {localIp}", localIp);
                        var errorMessageBytes = GetErrorMessageBytes(localIp, localPort);
                        await client.GetStream().WriteAsync(errorMessageBytes, 0, errorMessageBytes.Length, stoppingToken);
                        return;
                }
            }

            // Setup live TCP connection between client and downstream port to let data flow.
            await target.ConnectAsync(targetIp, targetPort, stoppingToken);
            var targetStream = target.GetStream();
            await targetStream.WriteAsync(buffer, 0, bytesRead, stoppingToken);
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

    /// <summary>
    /// Detects the HTTP/2 clear-text preface (24 bytes) in the initial client payload.
    /// This lets us route h2c vs. HTTP/1.1 traffic to different Kestrel ports.
    /// </summary>
    private static bool IsHttp2Preface(ReadOnlySpan<byte> buffer)
    {
        // Kestrel only support multiple protocols over HTTPS since the protocol selection happens during ALPN protocol negotiation.
        // We can however detect the HTTP/2 preface and use that to determine if the client is using HTTP/2 or HTTP/1.1 and route to
        // a specific backend by hosting HTTP/2 and HTTP/1 on different Kestrel ports.
        // https://httpwg.org/specs/rfc7540.html#ConnectionHeader
        // https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore?view=aspnetcore-10.0&tabs=visual-studio#protocol-negotiation
        // https://github.com/dotnet/aspnetcore/issues/13502

        const int prefaceLen = 24;
        return buffer.Length >= prefaceLen && buffer.Slice(0, prefaceLen).SequenceEqual(Http2Preface);
    }

    public static string ParseSniHostname(byte[] buffer, int length)
    {
        if (buffer == null || length < 5 || buffer[0] != 0x16)
        {
            return null; // Not a TLS handshake record
        }

        var pos = 5; // Skip: ContentType (1) + Version (2) + Record Length (2)
        
        if (pos + 1 > length || buffer[pos++] != 0x01)
        {
            return null; // Not a ClientHello message
        }

        if (pos + 3 + 2 + 32 > length)
        {
            return null; // Handshake Length + Version + Random
        }

        pos += 3; // Skip Handshake Length
        pos += 2; // Skip Protocol Version
        pos += 32; // Skip Random

        if (pos + 1 > length)
        {
            return null;
        }

        var sessionIdLength = buffer[pos++];
        pos += sessionIdLength;
        if (pos > length)
        {
            return null;
        }

        if (pos + 2 > length)
        {
            return null;
        }

        var cipherSuitesLength = (buffer[pos++] << 8) | buffer[pos++];
        pos += cipherSuitesLength;

        if (pos > length)
        {
            return null;
        }

        if (pos + 1 > length)
        {
            return null;
        }

        var compressionMethodsLength = buffer[pos++];
        pos += compressionMethodsLength;
        if (pos > length)
        {
            return null;
        }

        if (pos + 2 > length)
        {
            return null;
        }

        var extensionsLength = (buffer[pos++] << 8) | buffer[pos++];
        var extensionsEnd = pos + extensionsLength;
        if (extensionsEnd > length)
        {
            return null;
        }

        while (pos + 4 <= extensionsEnd)
        {
            var extensionType = (buffer[pos++] << 8) | buffer[pos++];
            var extensionLength = (buffer[pos++] << 8) | buffer[pos++];
            if (pos + extensionLength > extensionsEnd)
            {
                return null;
            }

            if (extensionType == 0x00) // Server Name Indication
            {
                var sniListLength = (buffer[pos++] << 8) | buffer[pos++];
                var sniListEnd = pos + sniListLength;
                if (sniListEnd > extensionsEnd)
                {
                    return null;
                }

                while (pos + 3 <= sniListEnd)
                {
                    var nameType = buffer[pos++];
                    var nameLength = (buffer[pos++] << 8) | buffer[pos++];

                    if (pos + nameLength > sniListEnd)
                    {
                        return null;
                    }

                    if (nameType == 0x00) // HostName
                    {
                        return Encoding.ASCII.GetString(buffer, pos, nameLength);
                    }

                    pos += nameLength;
                }
            }
            else
            {
                pos += extensionLength;
            }
        }

        return null;
    }
}


