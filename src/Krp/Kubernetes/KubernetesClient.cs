using k8s;
using k8s.Models;
using Krp.Endpoints.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Kubernetes;

public class KubernetesClient : IKubernetesClient
{
    private readonly ILogger<KubernetesClient> _logger;

    public KubernetesClient(ILogger<KubernetesClient> logger)
    {
        _logger = logger;
    }

    public bool TryGetKubeConfigPath(out string path)
    {
        path = KubernetesClientConfiguration.KubeConfigDefaultLocation;
        return File.Exists(path);
    }

    public async Task<string> FetchCurrentContext()
    {
        var config = await KubernetesClientConfiguration.LoadKubeConfigAsync();
        return config.CurrentContext ?? "unknown";
    }

    /// <summary>
    /// Fetches Kubernetes services and returns a list of <see cref="KubernetesEndpoint"/>.
    /// </summary>
    /// <param name="filters"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<List<KubernetesEndpoint>> FetchServices(List<Regex> filters, CancellationToken ct)
    {
        var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
        using var client = new k8s.Kubernetes(config);

        var namespaces = await client.ListNamespaceAsync(cancellationToken: ct);
        var endpoints = new ConcurrentBag<KubernetesEndpoint>();

        await Parallel.ForEachAsync(namespaces.Items, new ParallelOptions { MaxDegreeOfParallelism = 100, CancellationToken = ct }, async (ns, cancellationToken) =>
        {
            var result = await FetchServicesAsync(ns.Metadata.Name, client, cancellationToken);

            foreach (var service in result)
            {
                if (service.Spec?.Ports == null)
                {
                    continue;
                }

                var fullName = $"namespace/{service.Namespace()}/service/{service.Name()}";

                if (filters.Count != 0 && !filters.Any(regex => regex.IsMatch(fullName)))
                {
                    continue;
                }

                foreach (var port in service.Spec.Ports)
                {
                    var endpoint = new KubernetesEndpoint
                    {
                        Host = GetServiceHost(service),
                        LocalPort = 0,
                        Namespace = service.Namespace(),
                        RemotePort = port.Port,
                        Resource = $"service/{service.Name()}",
                    };

                    endpoints.Add(endpoint);
                }

                var podEndpoints = await FetchPodContainerPortEndpointsAsync(service, client, cancellationToken);
                foreach (var endpoint in podEndpoints)
                {
                    endpoints.Add(endpoint);
                }
            }
        });

        return endpoints.ToList();
    }
    
    private async Task<IEnumerable<V1Service>> FetchServicesAsync(string ns, k8s.Kubernetes client, CancellationToken ct)
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

    private async Task<IEnumerable<KubernetesEndpoint>> FetchPodContainerPortEndpointsAsync(V1Service service, k8s.Kubernetes client, CancellationToken ct)
    {
        if (service.Spec?.Selector == null || service.Spec.Selector.Count == 0)
        {
            return [];
        }

        var labelSelector = string.Join(",", service.Spec.Selector.Select(x => $"{x.Key}={x.Value}"));
        V1PodList pods;
        try
        {
            pods = await client.ListNamespacedPodAsync(service.Namespace(), labelSelector: labelSelector, cancellationToken: ct);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogInformation("namespace/{namespace}/service/{service} pods (skipping due to access)", service.Namespace(), service.Name());
            return [];
        }

        var servicePorts = service.Spec.Ports?.Select(x => x.Port).ToHashSet() ?? [];
        var endpoints = new List<KubernetesEndpoint>();

        foreach (var pod in pods.Items.OrderBy(x => x.Name()))
        {
            if (!string.Equals(pod.Status?.Phase, "Running", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var port in pod.Spec?.Containers?.SelectMany(x => x.Ports ?? []) ?? [])
            {
                if (servicePorts.Contains(port.ContainerPort))
                {
                    continue;
                }

                endpoints.Add(new KubernetesEndpoint
                {
                    Host = GetServiceHost(service),
                    LocalPort = 0,
                    Namespace = service.Namespace(),
                    RemotePort = port.ContainerPort,
                    Resource = $"pod/{pod.Name()}",
                });
            }
        }

        return endpoints;
    }

    private static string GetServiceHost(V1Service service)
    {
        return $"{service.Name()}.{service.Namespace()}";
    }
}
