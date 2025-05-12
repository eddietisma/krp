using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.Forwarders.HttpForwarder;

public class HttpForwarderBackgroundService : BackgroundService
{
    private readonly IServiceCollection _services;
    private readonly IServiceProvider _serviceProvider;

    public HttpForwarderBackgroundService(IServiceCollection services, IServiceProvider serviceProvider)
    {
        _services = services;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var host = new WebHost(_services, _serviceProvider);
        await host.Start(stoppingToken);
    }
}
