using k8s;
using k8s.Models;
using Krp.Endpoints;
using Krp.Endpoints.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        // Prevent running multiple endpoints discovery simultaneously.
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
                var result = await FetchServicesAsync(ns.Metadata.Name, client, cancellationToken);

                foreach (var svc in result)
                {
                    services.Add(svc);
                }
            });

            var filteredServices = services
                .Where(x => x.Spec?.Ports != null)
                .Where(x =>
                {
                    var fullName = $"namespace/{x.Namespace()}/service/{x.Name()}";
                    return _compiledFilters.Count == 0 || _compiledFilters.Any(regex => regex.IsMatch(fullName));
                });

            var endpoints = new List<KubernetesEndpoint>();

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

                    endpoints.Add(endpoint);
                }
            }

            _endpointManager.AddEndpoints(endpoints);
            _endpointManager.TriggerEndPointsChangedEvent();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IEnumerable<V1Service>> FetchServicesAsync(string ns, Kubernetes client, CancellationToken ct)
    {
        try
        {
            var namespaceServices = await client.ListNamespacedServiceAsync(ns, cancellationToken: ct);
            return namespaceServices.Items;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogInformation("namespace/{namespace} (skipping due to access)", ns);
            return Enumerable.Empty<V1Service>();
        }
    }
}