using Krp.Common;
using Krp.EndpointExplorer;
using Krp.Endpoints;
using Krp.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Dns;

public class DnsBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEndpointManager _endpointManager;
    private readonly ILogger<DnsBackgroundService> _logger;
    private readonly IDnsHandler _dnsHandler;
    private readonly ValidationState _validationState;

    public DnsBackgroundService(IServiceProvider serviceProvider, IEndpointManager endpointManager, IDnsHandler dnsHandler, ValidationState validationState, ILogger<DnsBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _endpointManager = endpointManager;
        _endpointManager.EndPointsChangedEvent += OnEndPointsChangedEvent;
        _dnsHandler = dnsHandler;
        _validationState = validationState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var validationSucceeded = await _validationState.WaitForCompletionAsync(stoppingToken);
        if (!validationSucceeded)
        {
            _logger.LogWarning("Skipping DNS updates because validation failed.");
            return;
        }

        _ = _dnsHandler.RunAsync(stoppingToken);

        var isEndpointExplorerEnabled = _serviceProvider.GetService<EndpointExplorerManager>() != null;
        if (!isEndpointExplorerEnabled)
        {
            // Skip updating DNS, since the endpoint explorer will once discovery is finished.
            // Prevents unnecessary updates where the static routes are updated first and then overwritten with dynamic ones.
            await UpdateDns();
        }
    }
    
    private async Task OnEndPointsChangedEvent()
    {
        await UpdateDns();
    }

    private async Task UpdateDns()
    {
        if (!_validationState.Succeeded)
        {
            _logger.LogWarning("Skipping DNS update because validation did not succeed.");
            return;
        }

        _logger.LogInformation("Updating DNS entries...");

        // https://kubernetes.io/docs/concepts/services-networking/dns-pod-service/#pods
        var hostnames = _endpointManager
            .GetAllHandlers()
            .OrderBy(x => x.LocalIp.ToUInt32()) // Sort to get deterministic order to prevent unnecessary DNS hosts changes.
            .Where(x => !string.IsNullOrEmpty(x.Url))
            .Select(x => $"{x.LocalIp} {x.Host}")
            .Distinct()
            .ToList();

        // Always refresh DNS entries, even when no endpoints are found (e.g. when switching to an empty cluster).
        await _dnsHandler.UpdateAsync(hostnames);
    }
}
