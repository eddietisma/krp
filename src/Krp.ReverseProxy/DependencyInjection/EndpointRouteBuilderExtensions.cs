using Krp.ReverseProxy.KubernetesForwarder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Krp.ReverseProxy.DependencyInjection;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapKubernetesForwarder(this IEndpointRouteBuilder builder)
    {
        builder.Map("/{**catch-all}", async (KubernetesRequestForwarder handler, HttpContext httpContext) =>
        {
            await handler.HandleRequest(httpContext);
        });

        return builder;
    }
}