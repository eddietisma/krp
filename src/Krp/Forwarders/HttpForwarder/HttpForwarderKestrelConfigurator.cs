using Krp.Endpoints;
using Krp.Https;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;

namespace Krp.Forwarders.HttpForwarder;

public class HttpForwarderKestrelConfigurator : IConfigureOptions<KestrelServerOptions>
{
    private readonly HttpForwarderOptions _options;
    private readonly ILogger<HttpForwarderKestrelConfigurator> _logger;
    private readonly IServiceProvider _provider;
    private readonly ICertificateManager _certificateManager;
    private readonly EndpointManager _endpointManager;

    public HttpForwarderKestrelConfigurator(
        IOptions<HttpForwarderOptions> forwarderOptions,
        ILogger<HttpForwarderKestrelConfigurator> logger,
        IServiceProvider provider,
        ICertificateManager certificateManager,
        EndpointManager endpointManager)
    {
        _options = forwarderOptions.Value;
        _logger = logger;
        _provider = provider;
        _certificateManager = certificateManager;
        _endpointManager = endpointManager;
    }

    public void Configure(KestrelServerOptions options)
    {
        options.ApplicationServices = _provider;
        switch (_options.InternalTransport)
        {
            case HttpForwarderInternalTransport.NamedPipe:
                ConfigureNamedPipes(options);
                break;
            case HttpForwarderInternalTransport.UnixSocket:
                ConfigureUnixSockets(options);
                break;
            default:
                ConfigureTcp(options);
                break;
        }

        if (!_certificateManager.TryCheckCertificateAuthority(out _))
        {
            // Skip HTTPS if no certificate is found.
            return;
        }

        ConfigureHttps(options);
    }

    private void ConfigureTcp(KestrelServerOptions options)
    {
        options.ListenAnyIP(_options.HttpPort, listen => listen.Protocols = HttpProtocols.Http1);
        options.ListenAnyIP(_options.Http2Port, listen => listen.Protocols = HttpProtocols.Http2);
    }

    private void ConfigureNamedPipes(KestrelServerOptions options)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("Named pipes are only supported on Windows. Falling back to TCP listeners.");
            ConfigureTcp(options);
            return;
        }

        options.ListenNamedPipe(_options.GetInternalPipeName(HttpForwarderInternalEndpoint.Http1), listen => listen.Protocols = HttpProtocols.Http1);
        options.ListenNamedPipe(_options.GetInternalPipeName(HttpForwarderInternalEndpoint.Http2), listen => listen.Protocols = HttpProtocols.Http2);
    }

    private void ConfigureUnixSockets(KestrelServerOptions options)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("Unix domain sockets are not supported on Windows. Falling back to TCP listeners.");
            ConfigureTcp(options);
            return;
        }

        var http1Path = _options.GetInternalUnixSocketPath(HttpForwarderInternalEndpoint.Http1);
        var http2Path = _options.GetInternalUnixSocketPath(HttpForwarderInternalEndpoint.Http2);
        EnsureUnixSocketPath(http1Path);
        EnsureUnixSocketPath(http2Path);
        options.ListenUnixSocket(http1Path, listen => listen.Protocols = HttpProtocols.Http1);
        options.ListenUnixSocket(http2Path, listen => listen.Protocols = HttpProtocols.Http2);
    }

    private void ConfigureHttps(KestrelServerOptions options)
    {
        Action<ListenOptions> configureHttps = listen =>
        {
            listen.UseHttps(httpsOptions =>
            {
                httpsOptions.ServerCertificateSelector = (connectionContext, serverName) =>
                {
                    try
                    {
                        var name = serverName;

                        // Some TLS clients omit SNI when connecting via an IP-literal URL (e.g. https://127.0.0.1),
                        // so we try to select a loopback IP certificate based on the connection endpoints.
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            var localAddress = (connectionContext?.LocalEndPoint as IPEndPoint)?.Address;
                            if (localAddress != null && IPAddress.IsLoopback(localAddress))
                            {
                                name = localAddress.ToString();
                            }
                            else
                            {
                                var remoteAddress = (connectionContext?.RemoteEndPoint as IPEndPoint)?.Address;
                                if (remoteAddress != null && IPAddress.IsLoopback(remoteAddress))
                                {
                                    name = remoteAddress.ToString();
                                }
                                else
                                {
                                    // Don't guess a certificate name without SNI; fail the handshake for non-loopback connections.
                                    return null;
                                }
                            }
                        }

                        return IsAllowedHost(name) ? _certificateManager.GetOrCreateServerCertificate(name) : null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve HTTPS certificate.");
                        return null;
                    }
                };
            });
            listen.Protocols = HttpProtocols.Http1;
        };

        switch (_options.InternalTransport)
        {
            case HttpForwarderInternalTransport.NamedPipe:
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    options.ListenNamedPipe(_options.GetInternalPipeName(HttpForwarderInternalEndpoint.Https), configureHttps);
                }
                break;
            case HttpForwarderInternalTransport.UnixSocket:
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var httpsPath = _options.GetInternalUnixSocketPath(HttpForwarderInternalEndpoint.Https);
                    EnsureUnixSocketPath(httpsPath);
                    options.ListenUnixSocket(httpsPath, configureHttps);
                }
                break;
            default:
                options.ListenAnyIP(_options.HttpsPort, configureHttps);
                break;
        }
    }

    private static void EnsureUnixSocketPath(string socketPath)
    {
        var dir = Path.GetDirectoryName(socketPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(socketPath))
        {
            File.Delete(socketPath);
        }
    }

    private bool IsAllowedHost(string hostName)
    {
        // Allow localhost.
        if (string.Equals(hostName, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Allow loopback ips.
        if (IPAddress.TryParse(hostName, out var ipAddress))
        {
            return IPAddress.IsLoopback(ipAddress);
        }

        // Allow endpoint hostnames.
        if (_endpointManager != null)
        {
            var handlers = _endpointManager.GetHandlerByHost(hostName);
            if (handlers.Any(handler => handler.LocalIp != null && IPAddress.IsLoopback(handler.LocalIp)))
            {
                return true;
            }
        }

        return false;
    }
}
