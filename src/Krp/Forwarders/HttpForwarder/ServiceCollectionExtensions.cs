using Krp.Dns;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using Yarp.ReverseProxy.Forwarder;

namespace Krp.Forwarders.HttpForwarder;

public static class ServiceCollectionExtensions
{
    public static void AddHttpForwarder(this IServiceCollection services, Action<HttpForwarderOptions> optionsAction)
    {
        services.AddSingleton<IConfigureOptions<KestrelServerOptions>, HttpForwarderKestrelConfigurator>();
        services.Configure(optionsAction);
        services.AddHttpForwarder();
        services.AddSingleton<HttpForwarder>();
        services.AddSingleton(sp =>
        {
            var dnsLookupHandler = sp.GetRequiredService<IDnsLookupHandler>();

            return new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                EnableMultipleHttp2Connections = true,
                ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
                ConnectTimeout = TimeSpan.FromSeconds(15),

                // Ignore all SSL certificate errors.
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                },

                ConnectCallback = async (ctx, ct) =>
                {
                    string host = ctx.DnsEndPoint.Host;

                    if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                        host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase))
                    {
                        var tcp = new TcpClient(AddressFamily.InterNetwork);
                        await tcp.ConnectAsync(host, ctx.DnsEndPoint.Port, ct);
                        return tcp.GetStream();        // ⇦ still pooled by host:port
                    }

                    var ip = await dnsLookupHandler.QueryAsync(host);
                    var sock = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    await sock.ConnectAsync(new IPEndPoint(ip, ctx.DnsEndPoint.Port), ct);
                    return new NetworkStream(sock, ownsSocket: true);
                }
            };
        });
    }

    public static void UseKubernetesForwarder(this IApplicationBuilder app)
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