using Krp.Endpoints;
using Krp.Https;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Net;

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
        options.ListenAnyIP(_options.HttpPort, listen => listen.Protocols = HttpProtocols.Http1);
        options.ListenAnyIP(_options.Http2Port, listen => listen.Protocols = HttpProtocols.Http2);

        if (!_certificateManager.TryCheckCertificateAuthority(out _))
        {
            // Skip HTTPS if no certificate is found.
            return;
        }

        options.ListenAnyIP(_options.HttpsPort, listen =>
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
        });
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
