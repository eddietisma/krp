using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Forwarders.TcpWithHttpForwarder;

public class TcpWithHttpForwarderBackgroundService : BackgroundService
{
    private readonly TcpWithHttpForwarder _tcpWithHttpForwarder;

    public TcpWithHttpForwarderBackgroundService(TcpWithHttpForwarder tcpWithHttpForwarder)
    {
        _tcpWithHttpForwarder = tcpWithHttpForwarder;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _tcpWithHttpForwarder.Start(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _tcpWithHttpForwarder.Stop();
        await base.StopAsync(cancellationToken);
    }
}
