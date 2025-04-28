using Krp.KubernetesForwarder.PortForward;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Yarp.ReverseProxy.Forwarder;

namespace Krp.KubernetesForwarder;

public class HttpForwarder
{
    private readonly PortForwardManager _portForwardManager;
    private readonly IHttpForwarder _forwarder;
    private readonly ILogger<HttpForwarder> _logger;

    public HttpForwarder(PortForwardManager portForwardManager, IHttpForwarder forwarder, ILogger<HttpForwarder> logger)
    {
        _portForwardManager = portForwardManager;
        _forwarder = forwarder;
        _logger = logger;
    }

    public async Task HandleRequest(HttpContext httpContext)
    {
        _logger.LogInformation("Received request from {requestUrl}", httpContext.Request.GetEncodedUrl());

        var requestUrl = httpContext.Request.Host.Host;

        var portForwardHandler = _portForwardManager.GetHandlerByUrl(requestUrl);
        if (portForwardHandler == null)
        {
            _logger.LogWarning("Invalid url for proxy request: {requestUrl}", requestUrl);
            await httpContext.Response.WriteAsync($"Invalid HTTP proxy request. No matching routing for: '{requestUrl}'");
            return;
        }

        await portForwardHandler.EnsureRunningAsync();

        var httpClient = new HttpMessageInvoker(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            EnableMultipleHttp2Connections = true,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        });

        var destinationUrl = $"http://localhost:{portForwardHandler.LocalPort}";

        _logger.LogInformation("Proxying {requestUrl} to {destinationUrl}", httpContext.Request.GetEncodedUrl(), destinationUrl);

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

