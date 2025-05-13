using k8s;
using Krp.EndpointExplorer;
using Krp.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.ContextSwitching;

public class ContextSwitchingWatcher : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly EndpointManager _endpointManager;
    private readonly ILogger<ContextSwitchingWatcher> _logger;
    private string _currentContext;

    public ContextSwitchingWatcher(IServiceProvider serviceProvider, EndpointManager endpointManager, ILogger<ContextSwitchingWatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _endpointManager = endpointManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var config = await KubernetesClientConfiguration.LoadKubeConfigAsync();

                if (string.IsNullOrEmpty(_currentContext))
                {
                    _logger.LogInformation("Starting dynamic proxying for Kubernetes cluster: {context}.", config.CurrentContext);
                    _currentContext = config.CurrentContext;
                }

                if (_currentContext != config.CurrentContext)
                {
                    _logger.LogInformation("Detected context switch from {oldContext} to {newContext}", _currentContext, config.CurrentContext);
                    _endpointManager.RemoveAllHandlers();

                    var endpointExplorer = _serviceProvider.GetService<EndpointExplorerHandler>();
                    if (endpointExplorer != null)
                    {
                        await endpointExplorer.DiscoverEndpointsAsync(ct);
                    }

                    _currentContext = config.CurrentContext;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch current context");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}