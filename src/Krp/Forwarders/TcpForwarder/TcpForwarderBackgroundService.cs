﻿using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Forwarders.TcpForwarder;

public class TcpForwarderBackgroundService : BackgroundService
{
    private readonly TcpForwarder _tcpForwarder;

    public TcpForwarderBackgroundService(TcpForwarder tcpForwarder)
    {
        _tcpForwarder = tcpForwarder;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _tcpForwarder.Start(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _tcpForwarder.Stop();
        await base.StopAsync(cancellationToken);
    }
}