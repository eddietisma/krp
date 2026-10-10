using Krp.EndpointExplorer;
using Krp.Endpoints;
using Krp.Kubernetes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Krp.ContextSwitching;

/// <summary>
/// Detects context switches in the Kubernetes environment and triggers endpoint rediscovery when detected.
/// </summary>
public class ContextSwitchingManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEndpointManager _endpointManager;
    private readonly IKubernetesClient _kbKubernetesClient;
    private readonly ILogger<ContextSwitchingManager> _logger;

    private string _currentContext = string.Empty;

    public ContextSwitchingManager(IServiceProvider serviceProvider, IEndpointManager endpointManager, IKubernetesClient kbKubernetesClient, ILogger<ContextSwitchingManager> logger)
    {
        _serviceProvider = serviceProvider;
        _endpointManager = endpointManager;
        _kbKubernetesClient = kbKubernetesClient;
        _logger = logger;
    }

    public async Task DetectContextSwitch()
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

                var endpointExplorer = _serviceProvider.GetService<EndpointExplorerManager>();
                endpointExplorer?.RequestRefresh();

                _currentContext = context;
            }
        }
        catch (OperationCanceledException)
        {
            // Suppress
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch current context");
        }
    }
}
