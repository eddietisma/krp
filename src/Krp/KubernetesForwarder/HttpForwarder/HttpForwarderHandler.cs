using Krp.KubernetesForwarder.Endpoints;
using Krp.KubernetesForwarder.PortForward;
using Krp.KubernetesForwarder.Routing;
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

namespace Krp.KubernetesForwarder.HttpForwarder;

/// <summary>
/// Uses Kestrel as HTTP server (configuration from appsettings.json) and forwards requests to Kubernetes based on domain.
/// </summary>
public class HttpForwarderHandler
{
    private readonly EndpointManager _portForwardManager;
    private readonly IHttpForwarder _forwarder;
    private readonly IDnsLookupHandler _dnsLookupHandler;
    private readonly ILogger<HttpForwarderHandler> _logger;

    public HttpForwarderHandler(EndpointManager portForwardManager, IHttpForwarder forwarder, IDnsLookupHandler dnsLookupHandler, ILogger<HttpForwarderHandler> logger)
    {
        _portForwardManager = portForwardManager;
        _forwarder = forwarder;
        _dnsLookupHandler = dnsLookupHandler;
        _logger = logger;
    }

    public async Task HandleRequest(HttpContext httpContext)
    {
        _logger.LogInformation("Received {requestUrl}", httpContext.Request.GetEncodedUrl());

        var endpoint = _portForwardManager.GetHttpEndpointByUrl(httpContext.Request.Host.Host, httpContext.Request.Path);
        var portForwardHandler = _portForwardManager.GetHandlerByUrl(httpContext.Request.Host.Host);

        if (portForwardHandler != null)
        {
            await portForwardHandler.EnsureRunningAsync();
        }

        var destinationUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

        if (portForwardHandler != null)
        {
            destinationUrl = $"http://localhost:{portForwardHandler.LocalPort}";
        }
   
        if (endpoint != null && !PortChecker.TryIsPortAvailable(endpoint.LocalPort))
        {
            destinationUrl = $"http://localhost:{endpoint.LocalPort}";

            if (httpContext.Request.Path.StartsWithSegments(endpoint.Path, out var remaining))
            {
                httpContext.Request.Path = remaining;
            }
        }

        //if (endpoint == null && portForwardHandler == null)
        //{
        //    _logger.LogWarning("Invalid url for proxy request: {requestUrl}", httpContext.Request.Host.Host);
        //    await httpContext.Response.WriteAsync($"Invalid HTTP proxy request. No matching routing for: '{httpContext.Request.Host.Host}'");
        //    return;
        //}

        _logger.LogInformation("Proxying {requestUrl} to {destinationUrl}", httpContext.Request.GetEncodedUrl(), destinationUrl);

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
    
        if (!destinationUrl.Contains("localhost"))
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

