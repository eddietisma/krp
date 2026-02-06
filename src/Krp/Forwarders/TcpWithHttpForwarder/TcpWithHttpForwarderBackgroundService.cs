using Krp.Validation;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Forwarders.TcpWithHttpForwarder;

public class TcpWithHttpForwarderBackgroundService : BackgroundService
{
    private readonly TcpWithHttpForwarder _tcpWithHttpForwarder;
    private readonly ValidationState _validationState;

    public TcpWithHttpForwarderBackgroundService(TcpWithHttpForwarder tcpWithHttpForwarder, ValidationState validationState)
    {
        _tcpWithHttpForwarder = tcpWithHttpForwarder;
        _validationState = validationState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _validationState.WaitForValidAsync(stoppingToken);
        await _tcpWithHttpForwarder.Start(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _tcpWithHttpForwarder.Stop();
        await base.StopAsync(cancellationToken);
    }
}
