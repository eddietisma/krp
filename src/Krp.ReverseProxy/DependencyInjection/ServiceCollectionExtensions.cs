using Krp.ReverseProxy.KubernetesForwarder;
using Krp.ReverseProxy.KubernetesForwarder.ContextSwitching;
using Krp.ReverseProxy.KubernetesForwarder.EndpointExplorer;
using Meziantou.Framework.Win32;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace Krp.ReverseProxy.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="T:Krp.ReverseProxy.KubernetesForwarder" /> service for Kubernetes forwarding.
    /// </summary>
    public static KubernetesForwarderBuilder AddKubernetesForwarder(this IServiceCollection services)
    {
        services.AddHttpForwarder();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#pragma warning disable CA1416
            var job = new JobObject();
            job.SetLimits(new JobObjectLimits { Flags = JobObjectLimitFlags.DieOnUnhandledException | JobObjectLimitFlags.KillOnJobClose, });
            services.AddSingleton(job);
#pragma warning restore CA1416
        }

        var builder = new KubernetesForwarderBuilder(services);

        services.AddHostedService<KubernetesContextSwitchingWatcher>();
        services.AddTransient<PortForwardHandler>();
        services.AddSingleton<ProcessRunner>();
        services.AddSingleton<KubernetesEndpointExplorer>();
        services.AddSingleton<KubernetesRequestForwarder>();
        services.AddSingleton(serviceProvider =>
        {
            var manager = new PortForwardHandlerManager(serviceProvider);

            foreach (var endpoint in builder.Endpoints)
            {
                manager.AddEndpoint(endpoint);
            }
            
            return manager;
        });

        return builder;
    }
}