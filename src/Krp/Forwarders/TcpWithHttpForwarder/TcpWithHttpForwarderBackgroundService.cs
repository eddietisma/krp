using Krp.Validation;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Forwarders.TcpWithHttpForwarder;

public class TcpWithHttpForwarderBackgroundService : BackgroundService
{
    private readonly TcpWithHttpForwarder _tcpWithHttpForwarder;
    private readonly ValidationState _validationState;
    private readonly IHostApplicationLifetime _lifetime;

    public TcpWithHttpForwarderBackgroundService(TcpWithHttpForwarder tcpWithHttpForwarder, ValidationState validationState, IHostApplicationLifetime lifetime)
    {
        _tcpWithHttpForwarder = tcpWithHttpForwarder;
        _validationState = validationState;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _validationState.WaitForValidAsync(stoppingToken);
        await WaitForApplicationStartedAsync(stoppingToken);
        await _tcpWithHttpForwarder.Start(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _tcpWithHttpForwarder.Stop();
        await base.StopAsync(cancellationToken);
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken cancellationToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(cancellationToken);
    }
}
