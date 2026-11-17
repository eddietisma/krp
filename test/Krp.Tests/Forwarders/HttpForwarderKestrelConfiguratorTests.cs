using Krp.Endpoints;
using Krp.Forwarders.HttpForwarder;
using Krp.Https;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;

namespace Krp.Tests.Forwarders;

[TestClass]
public sealed class HttpForwarderKestrelConfiguratorTests
{
    [TestMethod]
    public async Task Configure_ShouldExposeReachableBoundPorts_WhenListenersUseAutomaticAllocation()
    {
        var builder = WebApplication.CreateSlimBuilder();
        var options = Options.Create(new HttpForwarderOptions { HttpPort = 0, Http2Port = 0, HttpsPort = 0 });
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(Mock.Of<ICertificateManager>());
        builder.Services.AddSingleton(Mock.Of<IEndpointManager>());
        builder.Services.AddSingleton<IConfigureOptions<KestrelServerOptions>, HttpForwarderKestrelConfigurator>();
        await using var app = builder.Build();
        app.Run(context => context.Response.WriteAsync(context.Request.Protocol));
        await app.StartAsync();

        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        var httpPort = options.Value.HttpListener!.IPEndPoint!.Port;
        var http2Port = options.Value.Http2Listener!.IPEndPoint!.Port;
        Assert.AreEqual(0, options.Value.HttpPort);
        Assert.AreEqual(0, options.Value.Http2Port);
        Assert.AreNotEqual(0, httpPort);
        Assert.AreNotEqual(0, http2Port);
        Assert.AreNotEqual(httpPort, http2Port);
        Assert.AreEqual("HTTP/1.1", await client.GetStringAsync($"http://127.0.0.1:{httpPort}/"));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{http2Port}/")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        Assert.AreEqual("HTTP/2", await response.Content.ReadAsStringAsync());
    }
}
