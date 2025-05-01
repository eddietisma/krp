using Krp.DependencyInjection;
using Krp.KubernetesForwarder.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.PortForward;

public class PortForwardManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly List<PortForwardHandler> _handlers = new();
    private readonly ILogger<PortForwardManager> _logger;

    public PortForwardManager(IServiceProvider serviceProvider, ILogger<PortForwardManager> logger, IOptions<KubernetesForwarderOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        foreach (var endpoint in options.Value.Endpoints)
        {
            AddEndpoint(endpoint);
        }
    }

    public event Func<Task> EndPointsChangedEvent;

    public void AddEndpoint(KrpEndpoint endpoint)
    {
        var handler = _serviceProvider.GetService<PortForwardHandler>(); // PortForwardHandler is registered as transient so we get a new instance each time.
        handler.IsStatic = endpoint.IsStatic;
        handler.LocalIp = IPAddress.Parse($"127.0.0.{_handlers.Count}");
        handler.LocalPort = endpoint.LocalPort;
        handler.Namespace = endpoint.Namespace;
        handler.RemotePort = endpoint.RemotePort;
        handler.Resource = endpoint.Resource;
        handler.Type = endpoint.Type;

        if (_handlers.Any(x => x.Url == handler.Url))
        {
            _logger.LogWarning("Skipped already existing endpoint for {url}", handler.Url);
            return;
        }

        _handlers.Add(handler);
        _logger.LogDebug("Registered endpoint for {url}", handler.Url);
    }

    public PortForwardHandler GetHandlerByUrl(string url)
    {
        return _handlers.FirstOrDefault(x => x.Url == url);
    }

    public PortForwardHandler GetByIpPort(IPAddress ip)
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