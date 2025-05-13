using Krp.Common;
using Krp.Dns;
using Krp.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Yarp.ReverseProxy.Forwarder;

namespace Krp.Forwarders.HttpForwarder;

/// <summary>
/// Uses Kestrel as HTTP server and forwards requests to Kubernetes based on domain.
/// </summary>
public class HttpForwarder
{
    private readonly EndpointManager _endpointManager;
    private readonly IHttpForwarder _forwarder;
    private readonly IDnsLookupHandler _dnsLookupHandler;
    private readonly ILogger<HttpForwarder> _logger;

    public HttpForwarder(EndpointManager endpointManager, IHttpForwarder forwarder, IDnsLookupHandler dnsLookupHandler, ILogger<HttpForwarder> logger)
    {
        _endpointManager = endpointManager;
        _forwarder = forwarder;
        _dnsLookupHandler = dnsLookupHandler;
        _logger = logger;
    }

    public async Task HandleRequest(HttpContext httpContext)
    {
        var requestUrl = httpContext.Request.GetEncodedUrl();

        _logger.LogDebug("Received {requestUrl}", requestUrl);

        var destinationUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

        var httpProxyHandler = _endpointManager.GetHttpEndpointByUrl(httpContext.Request.Host.Host, httpContext.Request.Path);
        if (httpProxyHandler != null && !PortChecker.TryIsPortAvailable(httpProxyHandler.LocalPort))
        {
            if (httpContext.Request.Path.StartsWithSegments(httpProxyHandler.Path, out var remaining))
            {
                httpContext.Request.Path = remaining;
            }

            destinationUrl = httpProxyHandler.GetDestinationUrl();
        }

        var portForwardHandler = _endpointManager.GetPortForwardHandlerByHost(httpContext.Request.Host.Host);
        if (portForwardHandler != null)
        {
            await portForwardHandler.EnsureRunningAsync();
            destinationUrl = portForwardHandler.GetDestinationUrl();
        }
        
        _logger.LogInformation("Proxying {requestUrl} to {destinationUrl}", requestUrl, destinationUrl);

        var socketsHandler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            EnableMultipleHttp2Connections = true,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
    
        if (!destinationUrl.Contains("localhost") && !destinationUrl.Contains("host.docker.internal"))
        {
            socketsHandler.ConnectCallback = async (context, cancellationToken) =>
            {
                var ipAddress = await _dnsLookupHandler.QueryAsync(httpContext.Request.Host.Host);
                var socket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(new IPEndPoint(ipAddress, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            };
        }

        var httpClient = new HttpMessageInvoker(socketsHandler);

        var requestConfig = new ForwarderRequestConfig
        {
            Version = GetVersionFromRequest(httpContext.Request),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };

        var response = await _forwarder.SendAsync(httpContext, destinationUrl, httpClient, requestConfig);
        if (response != ForwarderError.None)
        {
            var errorFeature = httpContext.Features.Get<IForwarderErrorFeature>();
            var exception = errorFeature.Exception;

            _logger.LogError(exception, "Unknown error occurred while forwarding request to {destinationUrl}", destinationUrl);
        }
    }

    private static Version GetVersionFromRequest(HttpRequest request)
    {
        return request.Protocol switch
        {
            "HTTP/1.0" => HttpVersion.Version10,
            "HTTP/1.1" => HttpVersion.Version11,
            "HTTP/2" or "HTTP/2.0" => HttpVersion.Version20,
            "HTTP/3" or "HTTP/3.0" => HttpVersion.Version30,
            _ => HttpVersion.Version11,
        };
    }
}

