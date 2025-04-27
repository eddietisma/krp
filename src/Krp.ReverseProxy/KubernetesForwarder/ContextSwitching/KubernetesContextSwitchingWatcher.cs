using k8s;
using Krp.ReverseProxy.KubernetesForwarder.EndpointExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.ReverseProxy.KubernetesForwarder.ContextSwitching;

public class KubernetesContextSwitchingWatcher : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PortForwardHandlerManager _portForwardHandlerManager;
    private readonly ILogger<KubernetesContextSwitchingWatcher> _logger;
    private string _currentContext;

    public KubernetesContextSwitchingWatcher(IServiceProvider serviceProvider, PortForwardHandlerManager portForwardHandlerManager, ILogger<KubernetesContextSwitchingWatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _portForwardHandlerManager = portForwardHandlerManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();

                if (string.IsNullOrEmpty(_currentContext))
                {
                    _logger.LogInformation("Starting dynamic proxying for Kubernetes cluster: {context}.", config.CurrentContext);
                    _currentContext = config.CurrentContext;
                }

                if (_currentContext != config.CurrentContext)
                {
                    _logger.LogInformation("Detected context switch from {oldContext} to {newContext}", _currentContext, config.CurrentContext);
                    _portForwardHandlerManager.RemoveAll();

                    var endpointExplorer = _serviceProvider.GetService<KubernetesEndpointExplorer>();
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