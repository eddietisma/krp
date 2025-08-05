using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Forwarders.HttpForwarder;

public class HttpForwarderBackgroundService : BackgroundService
{
    private readonly HttpForwarderOptions _options;
    private readonly IServiceCollection _services;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HttpForwarderBackgroundService> _logger;

    public HttpForwarderBackgroundService(IOptions<HttpForwarderOptions> options, IServiceCollection services, IServiceProvider serviceProvider, ILogger<HttpForwarderBackgroundService> logger)
    {
        _options = options.Value;
        _services = services;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting web host for HttpForwarder");

        var host = new WebHost(_options, _services, _serviceProvider);
        await host.Start(stoppingToken);
    }
}
