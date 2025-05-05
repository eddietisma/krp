using k8s;
using k8s.Models;
using Krp.KubernetesForwarder.Endpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.EndpointExplorer;

public class EndpointExplorerHandler
{
    private readonly EndpointManager _endpointManager;
    private readonly ILogger<EndpointExplorerHandler> _logger;
    private readonly List<Regex> _compiledFilters = new();

    public EndpointExplorerHandler(IOptions<EndpointExplorerOptions> options, EndpointManager endpointManager, ILogger<EndpointExplorerHandler> logger)
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
        _logger.LogInformation("Discovering endpoints...");

        var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
        var client = new Kubernetes(config);
        var services = new List<V1Service>();

        var namespaces = await client.ListNamespaceAsync(cancellationToken: ct);
        foreach (var ns in namespaces.Items)
        {
            try
            {
                var namespaceServices = await client.ListNamespacedServiceAsync(ns.Name(), cancellationToken: ct);
                services.AddRange(namespaceServices.Items);
                _logger.LogInformation("namespace/{namespace}", ns.Name());
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogInformation("namespace/{namespace} (skipping due to access)", ns.Name());
            }
        }

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
                var endpoint = new KrpEndpoint
                {
                    LocalPort = 0,
                    Namespace = service.Namespace(),
                    RemotePort = port.Port,
                    Resource = service.Name(),
                    Type = "service",
                };

                _endpointManager.AddEndpoint(endpoint);
            }
        }

        _endpointManager.TriggerEndPointsChangedEvent();
    }
}