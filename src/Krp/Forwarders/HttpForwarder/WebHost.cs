using Krp.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Forwarders.HttpForwarder;

public class WebHost
{
    private readonly HttpForwarderOptions _options;
    private readonly IServiceCollection _services;
    private readonly IServiceProvider _serviceProvider;

    public WebHost(HttpForwarderOptions options, IServiceCollection services, IServiceProvider serviceProvider)
    {
        _options = options;
        _services = services;
        _serviceProvider = serviceProvider;
    }

    public async Task Start(CancellationToken stoppingToken)
    {
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.AddKrpLogger();
            })
            .ConfigureServices(services =>
            {
                // Forward all registered services to the web host.
                foreach (var serviceDescriptor in _services)
                {
                    // Skip non-internal registrations.
                    if (!serviceDescriptor.ServiceType.Namespace!.StartsWith(nameof(Krp)))
                    {
                        continue;
                    }

                    // Skip BackgroundService registrations to avoid duplicates.
                    if (typeof(BackgroundService).IsAssignableFrom(serviceDescriptor.ServiceType))
                    {
                        continue;
                    }
             
                    if (serviceDescriptor.Lifetime == ServiceLifetime.Singleton)
                    {
                        // Resolve the existing singleton instance
                        var instance = _serviceProvider.GetService(serviceDescriptor.ServiceType);
                        services.AddSingleton(serviceDescriptor.ServiceType, instance);
                        continue;
                    }

                    services.Add(serviceDescriptor);
                }
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.ListenAnyIP(_options.HttpPort, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1;
                    });

                    serverOptions.ListenAnyIP(_options.Http2Port, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });

                    serverOptions.ListenAnyIP(_options.HttpsPort, listenOptions =>
                    {
                        listenOptions.UseHttps(); // Use default dev certs for HTTPS.
                        listenOptions.Protocols = HttpProtocols.Http1;
                    });
                });
                webBuilder.UseStartup<Startup>();
            });

        await hostBuilder.Build().RunAsync(stoppingToken);
    }
}
