using Krp.KubernetesForwarder.Dns;
using Krp.KubernetesForwarder.EndpointExplorer;
using Krp.KubernetesForwarder.Models;
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
        builder.Endpoints.Add(new KrpEndpoint
        {
            LocalPort = localPort,
            Namespace = ns,
            RemotePort = remotePort,
            Resource = resource,
            Type = "service",
            IsStatic = true,
        });

        return builder;
    }

    /// <summary>
    /// Use automatic endpoint discovery.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="optionsAction"></param>
    /// <returns></returns>
    public static KubernetesForwarderBuilder UseEndpointExplorer(this KubernetesForwarderBuilder builder, Action<KubernetesEndpointExplorerOptions> optionsAction)
    {
        builder.Services.Configure(optionsAction);
        builder.Services.AddHostedService<KubernetesEndpointExplorerBackgroundService>();
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
        builder.Services.AddHostedService<DnsUpdateService>();

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
}