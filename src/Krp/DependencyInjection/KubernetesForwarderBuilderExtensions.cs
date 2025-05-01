using Krp.KubernetesForwarder.EndpointExplorer;
using Krp.KubernetesForwarder.HttpForwarder;
using Krp.KubernetesForwarder.Models;
using Krp.KubernetesForwarder.Routing;
using Krp.KubernetesForwarder.TcpForwarder;
using Krp.KubernetesForwarder.TcpWithHttpForwarder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace Krp.DependencyInjection;

public static class KubernetesBuilderExtension
{
    /// <summary>
    /// Define a new endpoint that will get proxied and port-forwarded when URL matches '{resource}.{ns}:{port}'.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="localPort">Use 0 for dynamic</param>
    /// <param name="remotePort"></param>
    /// <param name="ns"></param>
    /// <param name="resource"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseEndpoint(this KubernetesForwarderBuilder builder, int localPort, int remotePort, string ns, string resource)
    {
        builder.Services.Configure<KubernetesForwarderOptions>(options =>
        {
            options.Endpoints.Add(new KrpEndpoint
            {
                LocalPort = localPort,
                Namespace = ns,
                RemotePort = remotePort,
                Resource = resource,
                Type = "service",
                IsStatic = true,
            });
        });

        return builder;
    }

    /// <summary>
    /// Use automatic endpoint discovery.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="optionsAction"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseEndpointExplorer(this KubernetesForwarderBuilder builder, Action<EndpointExplorerOptions> optionsAction)
    {
        builder.Services.Configure(optionsAction);
        builder.Services.AddHostedService<EndpointExplorerBackgroundService>();
        return builder;
    }

    /// <summary>
    /// Use Windows HOSTS file DNS routing.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="routing"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseRouting(this KubernetesForwarderBuilder builder, KrpRouting routing)
    {
        builder.Services.AddHostedService<DnsUpdateBackgroundService>();

        switch (routing)
        {
            case KrpRouting.WindowsHostsFile:
                builder.Services.Configure<DnsWindowsHostsOptions>(options =>
                {
                    var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KRP_WINDOWS_HOSTS")))
                    {
                        path = Environment.GetEnvironmentVariable("KRP_WINDOWS_HOSTS");
                    }

                    options.Path = path;
                });
                builder.Services.AddSingleton<IDnsHandler, DnsWindowsHostsHandler>();
                break;
        }

        return builder;
    }

    /// <summary>
    /// Starts an optional Kestrel server to handle HTTP requests.
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseHttpForwarder(this KubernetesForwarderBuilder builder)
    {
        builder.Services.AddHostedService<HttpForwarderBackgroundService>();
        builder.Services.AddSingleton(builder.Services);
        return builder;
    }

    /// <summary>
    /// Starts an optional TCP server to handle TCP requests.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="optionsAction"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseTcpForwarder(this KubernetesForwarderBuilder builder, Action<TcpForwarderOptions> optionsAction)
    {
        builder.Services.Configure(optionsAction);
        builder.Services.AddHostedService<TcpForwarderBackgroundService>();
        return builder;
    }

    /// <summary>
    /// Starts an optional TCP server to handle TCP requests with HTTP packet inspection.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="optionsAction"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseTcpWithHttpForwarder(this KubernetesForwarderBuilder builder, Action<TcpForwarderOptions> optionsAction)
    {
        builder.Services.Configure(optionsAction);
        builder.Services.AddHostedService<TcpWithHttpForwarderBackgroundService>();
        builder.UseHttpForwarder();
        return builder;
    }
}