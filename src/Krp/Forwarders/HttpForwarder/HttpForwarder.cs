using Krp.Common;
using Krp.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Http;
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
    private readonly SocketsHttpHandler _socketsHttpHandler;
    private readonly ILogger<HttpForwarder> _logger;

    public HttpForwarder(EndpointManager endpointManager, IHttpForwarder forwarder, SocketsHttpHandler socketsHttpHandler, ILogger<HttpForwarder> logger)
    {
        _endpointManager = endpointManager;
        _forwarder = forwarder;
        _socketsHttpHandler = socketsHttpHandler;
        _logger = logger;
    }

    public async Task HandleRequest(HttpContext httpContext)
    {
        var requestUrl = httpContext.Request.GetEncodedUrl();

        _logger.LogDebug("Received request from {requestUrl}", requestUrl);

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

        var httpClient = new HttpMessageInvoker(_socketsHttpHandler);

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

