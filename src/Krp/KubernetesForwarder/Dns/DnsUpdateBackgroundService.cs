using Krp.KubernetesForwarder.Endpoints;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.Dns;

public class DnsUpdateBackgroundService : BackgroundService
{
    private readonly EndpointManager _endpointManager;
    private readonly ILogger<DnsUpdateBackgroundService> _logger;
    private readonly IDnsHandler _dnsHandler;

    public DnsUpdateBackgroundService(EndpointManager endpointManager, IDnsHandler dnsHandler, ILogger<DnsUpdateBackgroundService> logger)
    {
        _endpointManager = endpointManager;
        _endpointManager.EndPointsChangedEvent += OnEndPointsChangedEvent;
        _dnsHandler = dnsHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_endpointManager.GetAllHandlers().Count == 0)
        {
            return;
        }

        // Configures all static endpoints set at startup using UseEndpoint.
        _logger.LogInformation("Updating DNS entries...");
        await UpdateDns();
    }


    private async Task OnEndPointsChangedEvent()
    {
        if (_endpointManager.GetAllHandlers().Count == 0)
        {
            return;
        }

        // Configures all endpoints set using UseEndpointExplorer.
        _logger.LogInformation("Updating DNS entries due to new endpoint changes....");
        await UpdateDns();
    }

    private async Task UpdateDns()
    {
        var hostnames = _endpointManager
            .GetAllHandlers()
            .Where(x => !string.IsNullOrEmpty(x.Url))
            .Select(x => $"{x.LocalIp} {x.Hostname}")
            .ToList();

        await _dnsHandler.UpdateAsync(hostnames);
    }
} 