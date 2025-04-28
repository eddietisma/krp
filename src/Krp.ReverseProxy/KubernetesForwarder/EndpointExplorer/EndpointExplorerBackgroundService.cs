using Krp.KubernetesForwarder.Dns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.EndpointExplorer;

public class EndpointExplorerBackgroundService : BackgroundService
{
    private readonly EndpointExplorerHandler _explorer;
    private readonly EndpointExplorerOptions _options;
    private readonly ILogger<DnsUpdateService> _logger;

    public EndpointExplorerBackgroundService(EndpointExplorerHandler explorer, IOptions<EndpointExplorerOptions> options, ILogger<DnsUpdateService> logger)
    {
        _explorer = explorer;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _explorer.DiscoverEndpointsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh endpoints");
            }

            await Task.Delay(_options.RefreshInterval, ct);
        }
    }
}