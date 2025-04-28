using Krp.KubernetesForwarder.ContextSwitching;
using Krp.KubernetesForwarder.EndpointExplorer;
using Krp.KubernetesForwarder.PortForward;
using Meziantou.Framework.Win32;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace Krp.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="T:Krp.ReverseProxy.KubernetesForwarder" /> service for Kubernetes forwarding.
    /// </summary>
    public static KubernetesForwarderBuilder AddKubernetesForwarder(this IServiceCollection services)
    {

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#pragma warning disable CA1416
            var job = new JobObject();
            job.SetLimits(new JobObjectLimits { Flags = JobObjectLimitFlags.DieOnUnhandledException | JobObjectLimitFlags.KillOnJobClose, });
            services.AddSingleton(job);
#pragma warning restore CA1416
        }

        var builder = new KubernetesForwarderBuilder(services);

        services.AddHostedService<ContextSwitchingWatcher>();
        services.AddSingleton<EndpointExplorerHandler>();
        services.AddSingleton<PortForwardManager>();
        services.AddTransient<PortForwardHandler>();
        services.AddSingleton<ProcessRunner>();

        return builder;
    }
}