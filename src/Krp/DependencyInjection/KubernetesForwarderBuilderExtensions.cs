using Krp.Dns;
using Krp.EndpointExplorer;
using Krp.Endpoints.Models;
using Krp.Forwarders.HttpForwarder;
using Krp.Forwarders.TcpForwarder;
using Krp.Forwarders.TcpWithHttpForwarder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Runtime.InteropServices;

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
            options.Endpoints.Add(new KubernetesEndpoint
            {
                LocalPort = localPort,
                Namespace = ns,
                RemotePort = remotePort,
                Resource = resource,
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
    /// <param name="localScheme"></param>
    /// <param name="host"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseHttpEndpoint(this KubernetesForwarderBuilder builder, int localPort, string localScheme, string host, string path = null)
    {
        builder.Services.Configure<KubernetesForwarderOptions>(options =>
        {
            options.HttpEndpoints.Add(new HttpEndpoint
            {
                Host = host,
                LocalPort = localPort,
                LocalScheme = localScheme,
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
        builder.Services.AddSingleton<EndpointExplorer.EndpointExplorer>();
        builder.Services.AddHostedService<EndpointExplorerBackgroundService>();

        return builder;
    }

    /// <summary>
    /// Starts an optional Kestrel server to handle HTTP requests.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="optionsAction"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseHttpForwarder(this KubernetesForwarderBuilder builder, Action<HttpForwarderOptions> optionsAction = null)
    {
        optionsAction ??= options =>
        {
            options.Http2Port = 81;
            options.HttpPort = 80;
            options.HttpsPort = 443;
        };

        builder.Services.Configure(optionsAction);
        builder.Services.AddHostedService<HttpForwarderBackgroundService>();
        builder.Services.AddSingleton(builder.Services);
        return builder;
    }

    /// <summary>
    /// Configures routing.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="routing"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseRouting(this KubernetesForwarderBuilder builder, DnsOptions routing)
    {
        builder.Services.AddHostedService<DnsUpdateBackgroundService>();
        
        switch (routing)
        {
            case DnsOptions.HostsFile:
                var hostsPath = Environment.GetEnvironmentVariable("KRP_WINDOWS_HOSTS") ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts")
                    : "/etc/hosts");
                
                builder.Services.AddSingleton<IDnsHandler, DnsHostsHandler>();
                builder.Services.Configure<DnsHostsOptions>(o => o.Path = hostsPath);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(routing), routing, $"Invalid value for {nameof(DnsOptions)}");
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
        builder.Services.AddSingleton<TcpForwarder>();
        builder.Services.AddHostedService<TcpForwarderBackgroundService>();
        return builder;
    }

    /// <summary>
    /// Starts an optional TCP server to handle TCP requests with HTTP packet inspection. 
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="tcpOptionsAction"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseTcpWithHttpForwarder(this KubernetesForwarderBuilder builder, Action<TcpForwarderOptions> tcpOptionsAction)
    {
        builder.Services.Configure(tcpOptionsAction);
        builder.Services.AddSingleton<TcpWithHttpForwarder>();
        builder.Services.AddHostedService<TcpWithHttpForwarderBackgroundService>();
        builder.UseHttpForwarder(options =>
        {
            options.Http2Port = 82;
            options.HttpPort = 81;
            options.HttpsPort = 444;
        });
        return builder;
    }
}