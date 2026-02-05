using Krp.Validation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.EndpointExplorer;

public class EndpointExplorerBackgroundService : BackgroundService
{
    private readonly EndpointExplorerManager _explorer;
    private readonly ValidationState _validationState;
    private readonly EndpointExplorerOptions _options;
    private readonly ILogger<EndpointExplorerBackgroundService> _logger;

    public EndpointExplorerBackgroundService(EndpointExplorerManager explorer, ValidationState validationState, IOptions<EndpointExplorerOptions> options, ILogger<EndpointExplorerBackgroundService> logger)
    {
        _explorer = explorer;
        _validationState = validationState;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var validationSucceeded = await _validationState.WaitForCompletionAsync(stoppingToken);
        if (!validationSucceeded)
        {
            _logger.LogWarning("Skipping endpoint discovery because validation failed.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _explorer.DiscoverEndpointsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh endpoints");
            }

            await Task.Delay(_options.RefreshInterval, stoppingToken);
        }
    }
}