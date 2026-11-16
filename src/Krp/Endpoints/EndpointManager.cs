using Krp.DependencyInjection;
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
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Endpoints;

public class EndpointManager : IEndpointManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IEndpointHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<EndpointManager> _logger;
    private readonly KubernetesForwarderOptions _options;
    private int _ipCounter;

    public event Func<Task>? EndPointsChangedEvent;

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
        var endpointPath = string.IsNullOrEmpty(endpoint.Path) ? "" : endpoint.Path.TrimStart('/').TrimEnd('/');
        var existingLocalIp = _handlers.FirstOrDefault(x => string.Equals(x.Value.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase)).Value?.LocalIp;
       
        var handler = _serviceProvider.GetRequiredService<HttpProxyEndpointHandler>(); // HttpProxyEndpointHandler is registered as transient so we get a new instance each time.
        handler.IsStatic = true;
        handler.LocalIp = existingLocalIp ?? GetNextLoopbackIp();
        handler.LocalPort = endpoint.LocalPort;
        handler.LocalScheme = endpoint.LocalScheme;
        handler.Url = $"{endpoint.Host}/{endpointPath}";
        handler.Host = endpoint.Host;
        handler.Path = $"/{endpointPath}";

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
        if (!endpoint.Resource.StartsWith("service/") && !endpoint.Resource.StartsWith("pod/"))
        {
            _logger.LogWarning("Skipped registering endpoint for resource '{resource}'. Must start with 'service/' or 'pod/'", endpoint.Resource);
            return;
        }

        var host = !string.IsNullOrEmpty(endpoint.Host) ? endpoint.Host : $"{endpoint.Resource[(endpoint.Resource.IndexOf('/') + 1)..]}.{endpoint.Namespace}";
        var existingLocalIp = _handlers.FirstOrDefault(x => string.Equals(x.Value.Host, host, StringComparison.OrdinalIgnoreCase)).Value?.LocalIp;

        var handler = _serviceProvider.GetRequiredService<PortForwardEndpointHandler>(); // PortForwardHandler is registered as transient so we get a new instance each time.
        handler.IsStatic = endpoint.IsStatic;
        handler.Host = host;
        handler.LocalIp = existingLocalIp ?? GetNextLoopbackIp();
        handler.LocalPort = endpoint.LocalPort;
        handler.Namespace = endpoint.Namespace;
        handler.RemotePort = endpoint.RemotePort;
        handler.Resource = endpoint.Resource;

        if (_handlers.ContainsKey(handler.Url))
        {
            _logger.LogInformation("Skipped already existing endpoint for {url}", handler.Url);
            return;
        }

        _handlers.TryAdd(handler.Url, handler);
        _logger.LogInformation("Registered endpoint for {url}", handler.Url);
    }

    public void AddEndpoints(List<KubernetesEndpoint> endpoints)
    {
        // Sort to get deterministic order to prevent unnecessary DNS hosts changes.
        foreach (var endpoint in endpoints.OrderBy(x => x.Resource).ThenBy(x => x.RemotePort))
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
    public IEndpointHandler? GetHttpEndpointByUrl(string host, string path)
    {
        return _handlers
            .Where(x => x.Value.GetType() == typeof(HttpProxyEndpointHandler))
            .Where(x => string.Equals(x.Value.Host, host, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(x => path.StartsWith($"{x.Value.Path}/") || (path == x.Value.Path))
            .Value;
    }

    /// <summary>
    /// Used for xxx to find the correct handler by URL and path.
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public IEnumerable<IEndpointHandler> GetHandlerByHost(string host)
    {
        return _handlers
            .Where(x => string.Equals(x.Value.Host, host, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value);
    }

    /// <summary>
    /// Used for Kubernetes endpoints to find the correct handler by host and port.
    /// </summary>
    /// <param name="host"></param>
    /// <param name="port"></param>
    /// <returns></returns>
    public IEndpointHandler? GetPortForwardHandlerByHost(string host, int? port)
    {
        var handlers = _handlers
            .Where(x => x.Value.GetType() == typeof(PortForwardEndpointHandler))
            .Where(x => string.Equals(x.Value.Host, host, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .OfType<PortForwardEndpointHandler>()
            .ToList();

        if (port.HasValue)
        {
            return handlers.FirstOrDefault(x => x.RemotePort == port.Value);
        }

        return handlers.Count == 1 ? handlers[0] : null;
    }

    /// <summary>
    /// Used for low-level TCP forwarding by routing using loopback IPs to determine correct downstream port.
    /// </summary>
    /// <param name="ip"></param>
    /// <param name="port"></param>
    /// <returns></returns>
    public IEndpointHandler? GetHandlerByIpPort(IPAddress ip, int port)
    {
        return _handlers
            .Where(x => x.Value.GetType() == typeof(PortForwardEndpointHandler))
            .Select(x => x.Value)
            .OfType<PortForwardEndpointHandler>()
            .FirstOrDefault(x => Equals(x.LocalIp, ip) && x.RemotePort == port);
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

    public Task TriggerEndPointsChangedEventAsync()
    {
        if (EndPointsChangedEvent is null)
        {
            return Task.CompletedTask;
        }

        var handlerTasks = EndPointsChangedEvent
            .GetInvocationList()
            .Cast<Func<Task>>()
            .Select(handler =>
            {
                try
                {
                    return handler();
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
            })
            .ToArray();

        return handlerTasks.Length == 0
            ? Task.CompletedTask
            : Task.WhenAll(handlerTasks);
    }

    private IPAddress GetNextLoopbackIp()
    {
        var index = Interlocked.Increment(ref _ipCounter);
        return IPAddress.Parse($"127.0.{index / 255}.{(index % 255) + 1}");
    }
}
