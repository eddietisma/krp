using Krp.KubernetesForwarder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Krp.DependencyInjection;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapKubernetesForwarder(this IEndpointRouteBuilder builder)
    {
        builder.Map("/{**catch-all}", async (HttpForwarder handler, HttpContext httpContext) =>
        {
            await handler.HandleRequest(httpContext);
        });

        return builder;
    }
}