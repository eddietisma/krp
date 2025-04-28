using Krp.DependencyInjection;
using Krp.KubernetesForwarder;
using Krp.KubernetesForwarder.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Krp;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddKubernetesForwarder()
            .UseEndpoint(0, 80, "asgnmntattest", "assignment-attest-attestorder-grpcserver-api")
            .UseEndpoint(0, 80, "asgnmntattest", "assignment-attest-notification-grpcserver-api")
            .UseEndpoint(0, 80, "associdocs", "documents-api")
            .UseEndpoint(0, 80, "handover", "handover-api")
            .UseEndpoint(0, 80, "itc-actors", "actor-registry-api")
            .UseEndpoint(0, 80, "itc-users", "user-api")
            .UseEndpoint(0, 80, "sharedsvcs", "bisnodeintegration-grpc-api")
            .UseEndpoint(0, 80, "sharedsvcs", "customer-api")
            .UseEndpoint(0, 80, "sharedsvcs", "document-sign-grpc-api")
            .UseEndpoint(0, 80, "sharedsvcs", "occupant-api")
            .UseEndpoint(0, 80, "sharedsvcs", "person-api")
            .UseEndpoint(0, 80, "sharedsvcs", "person-economy-grpc-api")
            .UseEndpoint(0, 80, "sharedsvcs", "person-grpcserver-api")
            .UseEndpoint(0, 80, "sharedsvcs", "texthandler-api")
            .UseEndpoint(0, 80, "sharedsvcs", "pdfgenerator-api")
            .UseEndpointExplorer(options =>
            {
                options.Filter = ["namespace/meetings/*", "namespace/*/service/person*"];
                options.RefreshInterval = TimeSpan.FromHours(1);
            })
            .UseRouting(KrpRouting.WindowsHostsFile);
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseMiddleware<ProtocolVersionMiddleware>();

        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapKubernetesForwarder();
        });
    }
}

public class ProtocolVersionMiddleware
{
    private readonly RequestDelegate _next;

    public ProtocolVersionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var forwarder = context.RequestServices.GetService<HttpForwarder>();
        await forwarder.HandleRequest(context);
        
        await _next(context);
    }
}

