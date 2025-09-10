﻿using Krp.DependencyInjection;
using Krp.Endpoints.HttpProxy;
using Krp.Endpoints.Models;
using Krp.Endpoints.PortForward;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Krp.Endpoints;

public class EndpointManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IEndpointHandler> _handlers = new();
    private readonly ILogger<EndpointManager> _logger;
    private readonly KubernetesForwarderOptions _options;

    public event Func<Task> EndPointsChangedEvent;

    public EndpointManager(IServiceProvider serviceProvider, ILogger<EndpointManager> logger, IOptions<KubernetesForwarderOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }
    
    public void Initialize()
    {
        foreach (var endpoint in _options.HttpEndpoints)
        {
            AddEndpoint(endpoint);
        }

        foreach (var endpoint in _options.Endpoints)
        {
            AddEndpoint(endpoint);
        }
    }
    /// <summary>
    /// Create and add a new HTTP proxy handler.
    /// </summary>
    /// <param name="endpoint"></param>
    public void AddEndpoint(HttpEndpoint endpoint)
    {
        var handler = _serviceProvider.GetService<HttpProxyEndpointHandler>(); // HttpProxyEndpointHandler is registered as transient so we get a new instance each time.
        handler.IsStatic = true;
        handler.LocalIp = _handlers.FirstOrDefault(x => x.Value.Host == endpoint.Host).Value?.LocalIp ?? IPAddress.Parse($"127.0.0.{_handlers.Count + 1}"); // Re-use IP if already exists
        handler.LocalPort = endpoint.LocalPort;
        handler.LocalScheme = endpoint.LocalScheme;
        handler.Url = $"{endpoint.Host}{endpoint.Path}";
        handler.Host = endpoint.Host;
        handler.Path = endpoint.Path;
        
        if (_handlers.ContainsKey(handler.Url))
        {
            _logger.LogWarning("Skipped already existing HTTP endpoint for {url}", handler.Url);
            return;
        }

        _handlers.TryAdd(handler.Url, handler);
        _logger.LogInformation("Registered HTTP endpoint for {host}{path}", endpoint.Host, endpoint.Path);
    }

    /// <summary>
    /// Create and add a new Kubernetes port-forwarding handler.
    /// </summary>
    /// <param name="endpoint"></param>
    public void AddEndpoint(KubernetesEndpoint endpoint)
    {
        var handler = _serviceProvider.GetService<PortForwardEndpointHandler>(); // PortForwardHandler is registered as transient so we get a new instance each time.
        handler.IsStatic = endpoint.IsStatic;
        handler.LocalIp = IPAddress.Parse($"127.0.{_handlers.Count / 255}.{(_handlers.Count % 255) + 1}");
        handler.LocalPort = endpoint.LocalPort;
        handler.Namespace = endpoint.Namespace;
        handler.RemotePort = endpoint.RemotePort;
        handler.Resource = endpoint.Resource;

        if (_handlers.ContainsKey(handler.Url))
        {
            _logger.LogWarning("Skipped already existing endpoint for {url}", handler.Url);
            return;
        }

        _handlers.TryAdd(handler.Url, handler);
        _logger.LogInformation("Registered endpoint for {url}", handler.Url);
    }

    public void AddEndpoints(List<KubernetesEndpoint> endpoints)
    {
        // Sort to get deterministic order to prevent unnecessary DNS hosts changes.
        foreach (var endpoint in endpoints.OrderBy(x => x.Resource))
        {
            AddEndpoint(endpoint);
        }
    }

    /// <summary>
    /// Used for HTTP endpoints to find the correct handler by URL and path.
    /// </summary>
    /// <param name="host"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public IEndpointHandler GetHttpEndpointByUrl(string host, string path)
    {
        return _handlers.FirstOrDefault(x => x.Value.GetType() == typeof(HttpProxyEndpointHandler) && x.Value.Host == host && path.StartsWith($"{x.Value.Path}/")).Value;
    }

    /// <summary>
    /// Used for xxx to find the correct handler by URL and path.
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public IEnumerable<IEndpointHandler> GetHandlerByHost(string host)
    {
        return _handlers.Where(x => x.Value.Host == host).Select(x => x.Value);
    }

    /// <summary>
    /// Used for Kubernetes endpoints to find the correct handler by host.
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public IEndpointHandler GetPortForwardHandlerByHost(string host)
    {
        return _handlers.FirstOrDefault(x => x.Value.GetType() == typeof(PortForwardEndpointHandler) && Equals(x.Value.Host, host)).Value;
    }

    /// <summary>
    /// Used for low-level TCP forwarding by routing using loopback IPs to determine correct downstream port.
    /// </summary>
    /// <param name="ip"></param>
    /// <returns></returns>
    public IEndpointHandler GetHandlerByIpPort(IPAddress ip)
    {
        return _handlers.FirstOrDefault(x => x.Value.GetType() == typeof(PortForwardEndpointHandler) && Equals(x.Value.LocalIp, ip)).Value;
    }

    public IEnumerable<IEndpointHandler> GetAllHandlers()
    {
        return _handlers.Select(x => x.Value);
    }
    
    public void RemoveAllHandlers()
    {
        foreach (var handler in _handlers)
        {
            handler.Value.Dispose();  
        }

        foreach (var handler in _handlers.Where(x => !x.Value.IsStatic))
        {
            _handlers.TryRemove(handler);
        }
    }

    public void TriggerEndPointsChangedEvent()
    {
        EndPointsChangedEvent?.Invoke();
    }
}