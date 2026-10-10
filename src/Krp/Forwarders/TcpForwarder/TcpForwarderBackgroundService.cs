using Krp.Validation;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Forwarders.TcpForwarder;

public class TcpForwarderBackgroundService : BackgroundService
{
    private readonly TcpForwarder _tcpForwarder;
    private readonly ValidationState _validationState;

    public TcpForwarderBackgroundService(TcpForwarder tcpForwarder, ValidationState validationState)
    {
        _tcpForwarder = tcpForwarder;
        _validationState = validationState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _validationState.WaitForValidAsync(stoppingToken);
        await _tcpForwarder.Start(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _tcpForwarder.Stop();
        await base.StopAsync(cancellationToken);
    }
}
