using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Yarp.ReverseProxy.Forwarder;

namespace Krp.ReverseProxy.KubernetesForwarder;

public class KubernetesRequestForwarder
{
    private readonly PortForwardHandlerManager _portForwardHandlerManager;
    private readonly IHttpForwarder _forwarder;
    private readonly ILogger<IHttpForwarder> _logger;

    public KubernetesRequestForwarder(PortForwardHandlerManager portForwardHandlerManager, IHttpForwarder forwarder, ILogger<IHttpForwarder> logger)
    {
        _portForwardHandlerManager = portForwardHandlerManager;
        _forwarder = forwarder;
        _logger = logger;
    }

    public async Task HandleRequest(HttpContext httpContext)
    {
        _logger.LogInformation("Received request from {requestUrl}", httpContext.Request.GetEncodedUrl());

        var requestUrl = httpContext.Request.Host.Host;

        var portForwardHandler = _portForwardHandlerManager.GetByUrl(requestUrl);
        if (portForwardHandler == null)
        {
            _logger.LogWarning("Invalid url for proxy request: {requestUrl}", requestUrl);
            await httpContext.Response.WriteAsJsonAsync(new InvalidProxyRequest($"Invalid proxy request. No matching routing for URL: '{requestUrl}'"));
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

        var response = await _forwarder.SendAsync(httpContext, destinationUrl, httpClient);
        if (response != ForwarderError.None)
        {
            var errorFeature = httpContext.Features.Get<IForwarderErrorFeature>();
            var exception = errorFeature.Exception;

            _logger.LogError(exception, "Unknown error occurred while forwarding request to {destinationUrl}", destinationUrl);
        }
    }

    private record InvalidProxyRequest(string Message);
}