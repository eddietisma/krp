using Krp.ReverseProxy.KubernetesForwarder.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace Krp.ReverseProxy.DependencyInjection;

public class KubernetesForwarderBuilder
{
    public IServiceCollection Services { get; set; }
    public List<KrpEndpoint> Endpoints { get; set; } = new();

    public KubernetesForwarderBuilder(IServiceCollection services)
    {
        Services = services;
    }
}