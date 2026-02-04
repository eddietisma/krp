using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Krp.Validation;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.EndpointExplorer;

public class EndpointExplorerBackgroundService : BackgroundService
{
    private readonly EndpointExplorer _explorer;
    private readonly EndpointExplorerOptions _options;
    private readonly ILogger<EndpointExplorerBackgroundService> _logger;
    private readonly ValidationState _validationState;

    public EndpointExplorerBackgroundService(EndpointExplorer explorer, IOptions<EndpointExplorerOptions> options, ILogger<EndpointExplorerBackgroundService> logger, ValidationState validationState)
    {
        _explorer = explorer;
        _options = options.Value;
        _logger = logger;
        _validationState = validationState;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var validationSucceeded = await _validationState.WaitForCompletionAsync(ct);
        if (!validationSucceeded)
        {
            _logger.LogWarning("Skipping endpoint discovery because validation failed.");
            return;
        }

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

    public Task RunAsync(CancellationToken ct)
    {
        return ExecuteAsync(ct);
    }
}
