using Krp.DependencyInjection;
using Krp.Endpoints;
using Krp.Endpoints.HttpProxy;
using Krp.Endpoints.Models;
using Krp.Forwarders.HttpForwarder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using Yarp.ReverseProxy.Forwarder;

namespace Krp.Tests.Endpoints;

[TestClass]
public sealed class RootHttpEndpointTests
{
    [TestMethod]
    [DataRow(null, "/main.js")]
    [DataRow("", "/callback")]
    [DataRow("/", "/")]
    [DataRow("/", "/main.js")]
    [DataRow("/", "/assets/config.json")]
    [DataRow("/", "/callback")]
    public void GetHttpEndpointByUrl_ShouldMatchAllPaths_WhenRootIsConfigured(string configuredPath, string requestPath)
    {
        using var services = new ServiceCollection().AddTransient<HttpProxyEndpointHandler>().BuildServiceProvider();
        var manager = new EndpointManager(services, NullLogger<EndpointManager>.Instance, Options.Create(new KubernetesForwarderOptions()));
        manager.AddEndpoint(new HttpEndpoint { Host = "start.hsb.se", LocalPort = 4200, LocalScheme = "http", Path = configuredPath });

        Assert.IsNotNull(manager.GetHttpEndpointByUrl("start.hsb.se", requestPath));
        Assert.IsNull(manager.GetHttpEndpointByUrl("other.hsb.se", requestPath));
    }

    [TestMethod]
    public void GetHttpEndpointByUrl_ShouldPreferSpecificPath_WhenRootIsAlsoConfigured()
    {
        using var services = new ServiceCollection().AddTransient<HttpProxyEndpointHandler>().BuildServiceProvider();
        var manager = new EndpointManager(services, NullLogger<EndpointManager>.Instance, Options.Create(new KubernetesForwarderOptions()));
        manager.AddEndpoint(new HttpEndpoint { Host = "start.hsb.se", LocalPort = 4200, LocalScheme = "http", Path = "/" });
        manager.AddEndpoint(new HttpEndpoint { Host = "start.hsb.se", LocalPort = 5000, LocalScheme = "http", Path = "/api" });
        manager.AddEndpoint(new HttpEndpoint { Host = "start.hsb.se", LocalPort = 5001, LocalScheme = "http", Path = "/api/v2" });

        Assert.AreEqual(5000, manager.GetHttpEndpointByUrl("start.hsb.se", "/api/users")?.LocalPort);
        Assert.AreEqual(5001, manager.GetHttpEndpointByUrl("start.hsb.se", "/api/v2/users")?.LocalPort);
        Assert.AreEqual(4200, manager.GetHttpEndpointByUrl("start.hsb.se", "/api-other")?.LocalPort);
    }

    [TestMethod]
    public async Task HandleRequest_ShouldForwardPathsAndQueriesToEachLocalApp_WhenRootIsConfigured()
    {
        await using var start = CreateServer();
        start.Run(context => context.Response.WriteAsync($"start:{context.Request.Path}{context.Request.QueryString}"));
        await start.StartAsync();

        await using var meetings = CreateServer();
        meetings.Run(context => context.Response.WriteAsync($"meetings:{context.Request.Path}{context.Request.QueryString}"));
        await meetings.StartAsync();

        await using var fallback = CreateServer();
        fallback.Run(context => context.Response.WriteAsync("fallback"));
        await fallback.StartAsync();

        using var services = new ServiceCollection().AddLogging().AddTransient<HttpProxyEndpointHandler>().AddHttpForwarder().BuildServiceProvider();
        var manager = new EndpointManager(services, NullLogger<EndpointManager>.Instance, Options.Create(new KubernetesForwarderOptions()));
        manager.AddEndpoint(new HttpEndpoint { Host = "start.hsb.se", LocalPort = GetPort(start), LocalScheme = "http", Path = "/" });
        manager.AddEndpoint(new HttpEndpoint { Host = "meetings-web.srv.hsb.se", LocalPort = GetPort(meetings), LocalScheme = "http", Path = "/" });

        // Keep fallback traffic local too, so a regression never calls production.
        using var invoker = new HttpMessageInvoker(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectCallback = async (context, token) =>
            {
                var port = context.DnsEndPoint.Host == "localhost" ? context.DnsEndPoint.Port : GetPort(fallback);
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(IPAddress.Loopback, port, token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        });
        var forwarder = new HttpForwarder(manager, services.GetRequiredService<IHttpForwarder>(), invoker, NullLogger<HttpForwarder>.Instance);
        await using var proxy = CreateServer();
        proxy.Run(forwarder.HandleRequest);
        await proxy.StartAsync();

        using var client = new HttpClient();
        foreach (var (host, app) in new[] { ("start.hsb.se", "start"), ("meetings-web.srv.hsb.se", "meetings") })
        {
            foreach (var path in new[] { "/", "/main.js", "/assets/config.json", "/callback?code=test&state=123" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{proxy.Urls.Single()}{path}");
                request.Headers.Host = host;
                using var response = await client.SendAsync(request);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.AreEqual($"{app}:{path}", await response.Content.ReadAsStringAsync(), $"Incorrect destination or path for {host}{path}");
            }
        }
    }

    private static WebApplication CreateServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        return builder.Build();
    }

    private static int GetPort(WebApplication app) => new Uri(app.Urls.Single()).Port;
}
