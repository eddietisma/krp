using Microsoft.Extensions.DependencyInjection;

namespace Krp.DependencyInjection;

public class KubernetesForwarderBuilder
{
    public IServiceCollection Services { get; set; }

    public KubernetesForwarderBuilder(IServiceCollection services)
    {
        Services = services;
    }
}