using Krp.DependencyInjection;
using Krp.KubernetesForwarder.PortForward;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.Endpoints;

public class EndpointManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly List<PortForwardHandler> _handlers = new();
    private readonly List<KrpHttpEndpoint> _httpEndpoints = new();
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

    public void AddEndpoint(KrpHttpEndpoint endpoint)
    {
        _httpEndpoints.Add(endpoint);
        _logger.LogDebug("Registered HTTP endpoint for {host}{path}", endpoint.Host, endpoint.Path);
    }

    public void AddEndpoint(KrpEndpoint endpoint)
    {
        var handler = _serviceProvider.GetService<PortForwardHandler>(); // PortForwardHandler is registered as transient so we get a new instance each time.
        handler.IsStatic = endpoint.IsStatic;
        handler.LocalIp = IPAddress.Parse($"127.0.0.{_handlers.Count}");
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


    public KrpHttpEndpoint GetHttpEndpointByUrl(string url, string path)
    {
        return _httpEndpoints.FirstOrDefault(x => x.Host == url && path.StartsWith($"{x.Path}/"));
    }

    public PortForwardHandler GetHandlerByUrl(string url)
    {
        return _handlers.FirstOrDefault(x => x.Url == url);
    }

    public PortForwardHandler GetHandlerByIpPort(IPAddress ip)
    {
        return _handlers.FirstOrDefault(x => Equals(x.LocalIp, ip));
    }

    public List<PortForwardHandler> GetAllHandlers()
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
    
    public void TriggerEndPointsChangedEvent()
    {
        EndPointsChangedEvent?.Invoke();
    }
}