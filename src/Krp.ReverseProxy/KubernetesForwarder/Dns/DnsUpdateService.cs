using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.ReverseProxy.KubernetesForwarder.Dns;

public class DnsUpdateService : BackgroundService
{
    private readonly PortForwardHandlerManager _portForwardHandlerManager;
    private readonly ILogger<DnsUpdateService> _logger;
    private readonly IDnsHandler _dnsHandler;

    public DnsUpdateService(PortForwardHandlerManager portForwardHandlerManager, IDnsHandler dnsHandler, ILogger<DnsUpdateService> logger)
    {
        _portForwardHandlerManager = portForwardHandlerManager;
        _portForwardHandlerManager.EndPointsChangedEvent += OnEndPointsChangedEvent;
        _dnsHandler = dnsHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_portForwardHandlerManager.GetAll().Count == 0)
        {
            return;
        }

        // Configures all static endpoints set at startup using UseEndpoint.
        _logger.LogInformation("Updating DNS entries...");
        await UpdateDns();
    }


    private async Task OnEndPointsChangedEvent()
    {
        if (_portForwardHandlerManager.GetAll().Count == 0)
        {
            return;
        }

        // Configures all endpoints by set using UseEndpointExplorer.
        _logger.LogInformation("Updating DNS entries due to new endpoint changes....");
        await UpdateDns();
    }

    private async Task UpdateDns()
    {
        var hostnames = _portForwardHandlerManager
            .GetAll()
            .Where(x => !string.IsNullOrEmpty(x.Url))
            .Select(x => x.Hostname)
            .ToList();

        await _dnsHandler.UpdateAsync(hostnames);
    }
} 