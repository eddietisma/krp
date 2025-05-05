using Krp.KubernetesForwarder.EndpointExplorer;
using Krp.KubernetesForwarder.Endpoints;
using Krp.KubernetesForwarder.HttpForwarder;
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
    /// Use DNS lookups to resolve hostnames to IP addresses.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="optionsAction"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseDnsLookup(this KubernetesForwarderBuilder builder, Action<DnsLookupOptions> optionsAction)
    {
        builder.Services.AddSingleton<IDnsLookupHandler, DnsLookupHandler>();
        builder.Services.Configure(optionsAction);
        return builder;
    }

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
    /// Define a new HTTP endpoint that will get proxied when URL matches '{host}/{path}' and local port is active.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="localPort"></param>
    /// <param name="host"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseHttpEndpoint(this KubernetesForwarderBuilder builder, int localPort, string host, string path = null)
    {
        builder.Services.Configure<KubernetesForwarderOptions>(options =>
        {
            options.HttpEndpoints.Add(new KrpHttpEndpoint
            {
                Host = host,
                LocalPort = localPort,
                Path = path,
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
        builder.Services.AddSingleton<EndpointExplorerHandler>();

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