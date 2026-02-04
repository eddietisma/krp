using Krp.Endpoints;
using Krp.Kubernetes;
using Krp.Validation;
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
    private readonly ValidationState _validationState;
    private string _currentContext = string.Empty;

    public ContextSwitchingWatcher(IServiceProvider serviceProvider, EndpointManager endpointManager, KubernetesClient kbKubernetesClient, ILogger<ContextSwitchingWatcher> logger, ValidationState validationState)
    {
        _serviceProvider = serviceProvider;
        _endpointManager = endpointManager;
        _kbKubernetesClient = kbKubernetesClient;
        _logger = logger;
        _validationState = validationState;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var validationSucceeded = await _validationState.WaitForCompletionAsync(ct);
        if (!validationSucceeded)
        {
            _logger.LogWarning("Skipping context switch monitoring because validation failed.");
            return;
        }

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

    public Task RunAsync(CancellationToken ct)
    {
        return ExecuteAsync(ct);
    }
}
