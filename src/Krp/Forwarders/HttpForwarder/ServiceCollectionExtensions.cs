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
        services.AddHttpForwarder(); // Register YARP ReverseProxy.

        services.Configure(optionsAction);
        services.AddSingleton<IConfigureOptions<KestrelServerOptions>, HttpForwarderKestrelConfigurator>();
        services.AddSingleton<HttpForwarder>();
        services.AddSingleton(sp => new HttpMessageInvoker(sp.GetRequiredService<SocketsHttpHandler>()));
        services.AddSingleton(sp =>
        {
            var dnsLookupHandler = sp.GetRequiredService<IDnsLookupHandler>();
            var options = sp.GetRequiredService<IOptions<HttpForwarderOptions>>().Value;

            var sslOptions = new SslClientAuthenticationOptions();

            if (options.SkipCertificateValidation)
            {
                // Ignore all SSL certificate errors.
                sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            }

            return new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = false,
                EnableMultipleHttp2Connections = true,
                ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
                ConnectTimeout = TimeSpan.FromSeconds(15),
                SslOptions = sslOptions,
                ConnectCallback = async (ctx, ct) =>
                {
                    var host = ctx.DnsEndPoint.Host;
                    var port = ctx.DnsEndPoint.Port;

                    // Fast-path for local dev.
                    if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                        host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase))
                    {
                        var tcpLocal = new TcpClient(AddressFamily.InterNetwork)
                        {
                            NoDelay = false,
                        };

                        await tcpLocal.ConnectAsync(host, port, ct);
                        return tcpLocal.GetStream();
                    }

                    // Pass-through to actual IP addresses using DNS lookup for HTTP proxy endpoints.
                    var ip = await dnsLookupHandler.QueryAsync(host);
                    if (ip != null)
                    {
                        var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                        {
                            NoDelay = true,
                        };

                        await socket.ConnectAsync(new IPEndPoint(ip, port), ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }

                    // Fallback: Let OS DNS resolve the hostname.
                    var tcp = new TcpClient(AddressFamily.InterNetwork)
                    {
                        NoDelay = true,
                    };

                    await tcp.ConnectAsync(host, port, ct);
                    return tcp.GetStream();
                },
            };
        });
    }

    public static void UseKubernetesForwarder(this IApplicationBuilder app)
    {
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp == null || !IPAddress.IsLoopback(remoteIp))
            {
                // Let docker handle network isolation when running in a container,
                // since we may receive non-loopback connections due to docker NAT.
                if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Loopback connections only.");
                    return;
                }
            }

            await next();
        });
        app.UseEndpoints(endpoints =>
        {
            endpoints.Map("/{**catch-all}", async (HttpForwarder handler, HttpContext httpContext) =>
            {
                await handler.HandleRequest(httpContext);
            });
        });
    }
}