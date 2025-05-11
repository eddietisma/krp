using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.HttpForwarder;

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
        var hostBuilder = Host.CreateDefaultBuilder()
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
                    serverOptions.ListenAnyIP(81, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1;
                    });

                    serverOptions.ListenAnyIP(82, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });

                    serverOptions.ListenAnyIP(443, listenOptions =>
                    {
                        listenOptions.UseHttps(); // Use default dev certs for HTTPS.
                        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                    });
                });
                webBuilder.UseStartup<Startup>();
            });

        await hostBuilder.Build().RunAsync(stoppingToken);
    }
}
