using Krp.Common;
using Krp.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
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
    private readonly IEndpointManager _endpointManager;
    private readonly IHttpForwarder _forwarder;
    private readonly HttpMessageInvoker _httpMessageInvoker;
    private readonly ILogger<HttpForwarder> _logger;

    public HttpForwarder(IEndpointManager endpointManager, IHttpForwarder forwarder, HttpMessageInvoker httpMessageInvoker, ILogger<HttpForwarder> logger)
    {
        _endpointManager = endpointManager;
        _forwarder = forwarder;
        _httpMessageInvoker = httpMessageInvoker;
        _logger = logger;
    }

    public async Task HandleRequest(HttpContext httpContext)
    {
        var requestUrl = httpContext.Request.GetEncodedUrl();

        _logger.LogInformation("{method} {requestUrl}", httpContext.Request.Method, requestUrl);

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
        
        var requestConfig = new ForwarderRequestConfig
        {
            Version = GetVersionFromRequest(httpContext.Request),
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await _forwarder.SendAsync(httpContext, destinationUrl, _httpMessageInvoker, requestConfig);
        stopwatch.Stop();

        _logger.LogInformation("{method} {requestUrl} → {destinationUrl} [{statusCode}] {elapsedMs}ms", httpContext.Request.Method, requestUrl, destinationUrl, httpContext.Response.StatusCode, stopwatch.ElapsedMilliseconds);

        if (response != ForwarderError.None)
        {
            var exception = httpContext.Features.Get<IForwarderErrorFeature>()?.Exception;
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
