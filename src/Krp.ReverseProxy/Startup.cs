using Krp.DependencyInjection;
using Krp.KubernetesForwarder;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Krp;

public class Startup 
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpForwarder();
        services.AddSingleton<HttpForwarder>();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapKubernetesForwarder();
        });
    }
}
