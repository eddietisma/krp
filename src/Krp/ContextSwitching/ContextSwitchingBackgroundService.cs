using Krp.Validation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.ContextSwitching;

public class ContextSwitchingBackgroundService : BackgroundService
{
    private readonly ContextSwitchingManager _contextSwitchingManager;
    private readonly ValidationState _validationState;
    private readonly ILogger<ContextSwitchingBackgroundService> _logger;

    public ContextSwitchingBackgroundService(ContextSwitchingManager contextSwitchingManager, ValidationState validationState, ILogger<ContextSwitchingBackgroundService> logger)
    {
        _contextSwitchingManager = contextSwitchingManager;
        _validationState = validationState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var succeeded = await _validationState.WaitForCompletionAsync(stoppingToken);
        if (!succeeded)
        {
            _logger.LogWarning("Skipping context switch monitoring because validation failed");
            return;
        }
        
        while (!stoppingToken.IsCancellationRequested)
        {
            await _contextSwitchingManager.DetectContextSwitch();
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
