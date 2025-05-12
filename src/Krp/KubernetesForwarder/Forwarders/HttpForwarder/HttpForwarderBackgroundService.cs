using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.Forwarders.HttpForwarder;

public class HttpForwarderBackgroundService : BackgroundService
{
    private readonly HttpForwarderOptions _options;
    private readonly IServiceCollection _services;
    private readonly IServiceProvider _serviceProvider;

    public HttpForwarderBackgroundService(IOptions<HttpForwarderOptions> options, IServiceCollection services, IServiceProvider serviceProvider)
    {
        _options = options.Value;
        _services = services;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var host = new WebHost(_options, _services, _serviceProvider);
        await host.Start(stoppingToken);
    }
}
