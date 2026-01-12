
using Krp.Endpoints;
using Krp.Kubernetes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.EndpointExplorer;

public class EndpointExplorer
{
    private readonly EndpointManager _endpointManager;
    private readonly KubernetesClient _kubernetesClient;
    private readonly ILogger<EndpointExplorer> _logger;
    private readonly List<Regex> _compiledFilters = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public EndpointExplorer(EndpointManager endpointManager, KubernetesClient kubernetesClient, IOptions<EndpointExplorerOptions> options, ILogger<EndpointExplorer> logger)
    {
        _endpointManager = endpointManager;
        _kubernetesClient = kubernetesClient;
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

            var endpoints = await _kubernetesClient.FetchServices(_compiledFilters, ct);
            _endpointManager.AddEndpoints(endpoints.ToList());
            await _endpointManager.TriggerEndPointsChangedEventAsync();
        }
        finally
        {
            _lock.Release();
        }
    }
}