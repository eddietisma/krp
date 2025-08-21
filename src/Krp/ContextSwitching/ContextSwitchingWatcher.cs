using Krp.Endpoints;
using Krp.Kubernetes;
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
    private readonly KubernetesClient _kbKubernetesClient;
    private readonly ILogger<ContextSwitchingWatcher> _logger;
    private string _currentContext;

    public ContextSwitchingWatcher(IServiceProvider serviceProvider, EndpointManager endpointManager, KubernetesClient kbKubernetesClient, ILogger<ContextSwitchingWatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _endpointManager = endpointManager;
        _kbKubernetesClient = kbKubernetesClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _kbKubernetesClient.FetchCurrentContext();

                if (string.IsNullOrEmpty(_currentContext))
                {
                    _currentContext = context;
                }

                if (_currentContext != context)
                {
                    _logger.LogInformation("Detected context switch from {oldContext} to {newContext}", _currentContext, context);
                    _endpointManager.RemoveAllHandlers();

                    var endpointExplorer = _serviceProvider.GetService<EndpointExplorer.EndpointExplorer>();
                    if (endpointExplorer != null)
                    {
                        await endpointExplorer.DiscoverEndpointsAsync(ct);
                    }

                    _currentContext = context;
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