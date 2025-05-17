using Krp.DependencyInjection;
using Krp.Endpoints.HttpProxy;
using Krp.Endpoints.Models;
using Krp.Endpoints.PortForward;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Krp.Endpoints;

public class EndpointManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly List<IEndpointHandler> _handlers = new();
    private readonly ILogger<EndpointManager> _logger;
    public EndpointManager(IServiceProvider serviceProvider, ILogger<EndpointManager> logger, IOptions<KubernetesForwarderOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        foreach (var endpoint in options.Value.HttpEndpoints)
        {
            AddEndpoint(endpoint);
        }

        foreach (var endpoint in options.Value.Endpoints)
        {
            AddEndpoint(endpoint);
        }
    }

    public event Func<Task> EndPointsChangedEvent;

    /// <summary>
    /// Create and add a new HTTP proxy handler.
    /// </summary>
    /// <param name="endpoint"></param>
    public void AddEndpoint(HttpEndpoint endpoint)
    {
        var handler = new HttpProxyEndpointHandler
        {
            IsStatic = true,
            LocalIp = _handlers.FirstOrDefault(x => x.Host == endpoint.Host)?.LocalIp ?? IPAddress.Parse($"127.0.0.{_handlers.Count + 1}"), // Re-use IP if already exists.
            LocalPort = endpoint.LocalPort,
            Url = $"{endpoint.Host}{endpoint.Path}",
            Host = endpoint.Host,
            Path = endpoint.Path,
        };

        if (_handlers.Any(x => x.Url == handler.Url))
        {
            _logger.LogWarning("Skipped already existing HTTP endpoint for {url}", handler.Url);
            return;
        }

        _handlers.Add(handler);
        _logger.LogDebug("Registered HTTP endpoint for {host}{path}", endpoint.Host, endpoint.Path);
    }

    /// <summary>
    /// Create and add a new Kubernetes port-forwarding handler.
    /// </summary>
    /// <param name="endpoint"></param>
    public void AddEndpoint(KubernetesEndpoint endpoint)
    {
        var handler = _serviceProvider.GetService<PortForwardEndpointHandler>(); // PortForwardHandler is registered as transient so we get a new instance each time.
        handler.IsStatic = endpoint.IsStatic;
        handler.LocalIp = IPAddress.Parse($"127.0.0.{_handlers.Count + 1}");
        handler.LocalPort = endpoint.LocalPort;
        handler.Namespace = endpoint.Namespace;
        handler.RemotePort = endpoint.RemotePort;
        handler.Resource = endpoint.Resource;

        if (_handlers.Any(x => x.Url == handler.Url))
        {
            _logger.LogWarning("Skipped already existing endpoint for {url}", handler.Url);
            return;
        }

        _handlers.Add(handler);
        _logger.LogDebug("Registered endpoint for {url}", handler.Url);
    }

    /// <summary>
    /// Used for HTTP endpoints to find the correct handler by URL and path.
    /// </summary>
    /// <param name="host"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public IEndpointHandler GetHttpEndpointByUrl(string host, string path)
    {
        return _handlers.FirstOrDefault(x => x.GetType() == typeof(HttpProxyEndpointHandler) && x.Host == host && path.StartsWith($"{x.Path}/"));
    }

    /// <summary>
    /// Used for Kubernetes endpoints to find the correct handler by host.
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public List<IEndpointHandler> GetHandlersByHost(string host)
    {
        return _handlers.Where(x => x.Host == host).ToList();
    }

    /// <summary>
    /// Used for Kubernetes endpoints to find the correct handler by host.
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public IEndpointHandler GetPortForwardHandlerByHost(string host)
    {
        return _handlers.FirstOrDefault(x => x.GetType() == typeof(PortForwardEndpointHandler) && x.Host == host);
    }

    /// <summary>
    /// Used for low-level TCP forwarding by routing using loopback IPs to determine correct downstream port.
    /// </summary>
    /// <param name="ip"></param>
    /// <returns></returns>
    public IEndpointHandler GetHandlerByIpPort(IPAddress ip)
    {
        return _handlers.FirstOrDefault(x => x.GetType() == typeof(PortForwardEndpointHandler) && Equals(x.LocalIp, ip));
    }

    public List<IEndpointHandler> GetAllHandlers()
    {
        return _handlers;
    }

    public void RemoveAllHandlers()
    {
        foreach (var handler in _handlers)
        {
            handler.Dispose();
        }

        _handlers.RemoveAll(handler => !handler.IsStatic);
    }

    public void RemovePortForwardHandlerByResource(string resource)
    {
        var handlers = _handlers.Where(x => x.GetType() == typeof(PortForwardEndpointHandler));

        foreach (var endpointHandler in handlers)
        {
            var handler = (PortForwardEndpointHandler)endpointHandler;
            if (!handler.Resource.Equals(resource))
            {
                continue;
            }

            _logger.LogDebug("Removing port-forward handler for {resource}", resource);
            handler.Dispose();
        }

        _handlers.RemoveAll(handler => !handler.IsStatic);
    }

    public void TriggerEndPointsChangedEvent()
    {
        EndPointsChangedEvent?.Invoke();
    }
}