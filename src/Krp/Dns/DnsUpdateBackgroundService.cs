using Krp.Common;
using Krp.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Dns;

public class DnsUpdateBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly EndpointManager _endpointManager;
    private readonly ILogger<DnsUpdateBackgroundService> _logger;
    private readonly IDnsHandler _dnsHandler;

    public DnsUpdateBackgroundService(IServiceProvider serviceProvider, EndpointManager endpointManager, IDnsHandler dnsHandler, ILogger<DnsUpdateBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _endpointManager = endpointManager;
        _endpointManager.EndPointsChangedEvent += OnEndPointsChangedEvent;
        _dnsHandler = dnsHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_endpointManager.GetAllHandlers().Any())
        {
            return;
        }

        // Configures all static endpoints set at startup using UseEndpoint.
        _logger.LogInformation("Updating DNS entries...");

        var isEndpointExplorerEnabled = _serviceProvider.GetService<EndpointExplorer.EndpointExplorer>() != null;
        if (!isEndpointExplorerEnabled)
        {
            // Skip updating DNS, since the endpoint explorer will, once discovery is finished.
            // Prevents unnecessary updates where the static routes are updated first and then overwritten with dynamic ones.
            await UpdateDns();
        }
    }
    
    private async Task OnEndPointsChangedEvent()
    {
        if (!_endpointManager.GetAllHandlers().Any())
        {
            return;
        }

        // Configures all endpoints set using UseEndpointExplorer.
        _logger.LogInformation("Updating DNS entries...");
        await UpdateDns();
    }

    private async Task UpdateDns()
    {
        // https://kubernetes.io/docs/concepts/services-networking/dns-pod-service/#pods
        var hostnames = _endpointManager
            .GetAllHandlers()
            .OrderBy(x => x.LocalIp.ToUInt32()) // Sort to get deterministic order to prevent unnecessary DNS hosts changes.
            .Where(x => !string.IsNullOrEmpty(x.Url))
            .Select(x => $"{x.LocalIp} {x.Host}")
            .Distinct()
            .ToList();

        await _dnsHandler.UpdateAsync(hostnames);
    }
}