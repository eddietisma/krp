using Krp.KubernetesForwarder.ContextSwitching;
using Krp.KubernetesForwarder.Endpoints;
using Krp.KubernetesForwarder.PortForward;
using Meziantou.Framework.Win32;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Krp.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="T:Krp.ReverseProxy.KubernetesForwarder" /> service for Kubernetes forwarding.
    /// </summary>
    public static KubernetesForwarderBuilder AddKubernetesForwarder(this IServiceCollection services, IConfiguration configuration)
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
        RegisterEndpoints(configuration, builder);

        services.AddHostedService<ContextSwitchingWatcher>();
        services.AddSingleton<EndpointManager>();
        services.AddTransient<PortForwardHandler>();
        services.AddSingleton<ProcessRunner>();

        return builder;
    }

    private static void RegisterEndpoints(IConfiguration configuration, KubernetesForwarderBuilder builder)
    {
        var httpEndpoints = configuration.GetSection("Krp:HttpEndpoints").Get<List<KrpHttpEndpoint>>();
        var endpoints = configuration.GetSection("Krp:Endpoints").Get<List<KrpEndpoint>>();

        foreach (var endpoint in httpEndpoints)
        {
            builder.UseHttpEndpoint(endpoint.LocalPort, endpoint.Host, endpoint.Path);
        }

        foreach (var endpoint in endpoints)
        {
            builder.UseEndpoint(endpoint.LocalPort, endpoint.RemotePort, endpoint.Namespace, endpoint.Resource);
        }
    }
}