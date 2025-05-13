using Krp.Forwarders.HttpForwarder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
            endpoints.Map("/{**catch-all}", async (HttpForwarder handler, HttpContext httpContext) =>
            {
                await handler.HandleRequest(httpContext);
            });
        });
    }
}