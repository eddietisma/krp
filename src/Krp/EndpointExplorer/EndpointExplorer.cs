using k8s;
using k8s.Autorest;
using k8s.Models;
using Krp.Endpoints;
using Krp.Endpoints.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.EndpointExplorer;

public class EndpointExplorer
{
    private readonly EndpointManager _endpointManager;
    private readonly ILogger<EndpointExplorer> _logger;
    private readonly List<Regex> _compiledFilters = [];
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private CancellationTokenSource _watchCts;

    public EndpointExplorer(IOptions<EndpointExplorerOptions> options, EndpointManager endpointManager, ILogger<EndpointExplorer> logger)
    {
        _endpointManager = endpointManager;
        _logger = logger;

        foreach (var pattern in options.Value.Filter)
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            _compiledFilters.Add(new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
        }
    }

    public async Task DiscoverEndpointsAsync(CancellationToken ct)
    {
        // Prevent running discovery simultaneously (e.g. with refresh and context switch).
        await _lock.WaitAsync(ct);

        try
        {
            _logger.LogInformation("Discovering endpoints...");

            var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
            var client = new Kubernetes(config);
            var services = new ConcurrentBag<V1Service>();

            var namespaces = await client.ListNamespaceAsync(cancellationToken: ct);

            await Parallel.ForEachAsync(namespaces.Items, new ParallelOptions { MaxDegreeOfParallelism = 100, CancellationToken = ct }, async (ns, cancellationToken) =>
            {
                try
                {
                    var namespaceServices = await client.ListNamespacedServiceAsync(ns.Metadata.Name, cancellationToken: cancellationToken);
                    _logger.LogInformation("namespace/{namespace}", ns.Metadata.Name);

                    foreach (var svc in namespaceServices.Items)
                    {
                        services.Add(svc);
                    }
                }
                catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
                {
                    _logger.LogInformation("namespace/{namespace} (skipping due to access)", ns.Metadata.Name);
                }
            });

            AddEndpoint(services);
            await StartWatcher(services, client, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void AddEndpoint(ConcurrentBag<V1Service> services)
    {
        var filteredServices = services
            .Where(x => x.Spec?.Ports != null)
            .Where(x =>
            {
                var fullName = $"namespace/{x.Namespace()}/service/{x.Name()}";
                return _compiledFilters.Count == 0 || _compiledFilters.Any(regex => regex.IsMatch(fullName));
            });

        foreach (var service in filteredServices)
        {
            foreach (var port in service.Spec.Ports)
            {
                var endpoint = new KubernetesEndpoint
                {
                    LocalPort = 0,
                    Namespace = service.Namespace(),
                    RemotePort = port.Port,
                    Resource = $"service/{service.Name()}",
                };

                _endpointManager.AddEndpoint(endpoint);
            }
        }

        _endpointManager.TriggerEndPointsChangedEvent();
    }

    private async Task StartWatcher(ConcurrentBag<V1Service> services, Kubernetes client, CancellationToken ct)
    {
        // _watchCts is used to cancel the watch when a new discovery is triggered.
        if (_watchCts is not null)
        {
            await _watchCts.CancelAsync();
            _watchCts.Dispose();
        }

        _watchCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _watchCts.Token);

        foreach (var ns in services.Select(x => x.Metadata.NamespaceProperty).Distinct())
        {
            var response = client.CoreV1.ListNamespacedServiceWithHttpMessagesAsync(ns, watch: true, cancellationToken: linkedCts.Token);

            try
            {
                await foreach (var (type, item) in response.WatchAsync<V1Service, V1ServiceList>(cancellationToken: linkedCts.Token).ConfigureAwait(false))
                {
                    _logger.LogInformation("Watcher event triggered for namespace {namespace}, type: {eventType} and resource: {resource}", ns, type, item.Metadata.Name);

                    switch (type)
                    {
                        case WatchEventType.Added:
                            AddEndpoint(new ConcurrentBag<V1Service> { item });
                            break;
                        case WatchEventType.Deleted:
                            _endpointManager.RemovePortForwardHandlerByResource($"service/{item.Name()}");
                            _endpointManager.TriggerEndPointsChangedEvent();
                            break;
                        case WatchEventType.Modified:
                            break;
                        case WatchEventType.Error:
                            break;
                        case WatchEventType.Bookmark:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Watcher for namespace {namespace} failed", ns);
                throw;
            }
        }
    }
}