using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Krp.IntegrationTests;

[TestClass]
[TestCategory("Docker")]
public sealed class ContainerHttpForwarderTests
{
    private INetwork _network;
    private IContainer _backend;
    private IContainer _upstream;
    private IContainer _proxy;
    private HttpClient _client;

    public TestContext TestContext { get; set; }

    [TestInitialize]
    public async Task Setup()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _network = new NetworkBuilder().Build();
        await _network.CreateAsync(timeout.Token);
        _backend = CreateServer("backend");
        _upstream = CreateServer("upstream");
        await Task.WhenAll(_backend.StartAsync(timeout.Token), _upstream.StartAsync(timeout.Token));

        // The backend is published on the Docker host, outside the proxy's network namespace.
        // Docker Engine resolves host-gateway, including when the test daemon runs inside DinD.
        // HTTP-only routes need a kubeconfig file, but no cluster connection or discovery.
        const string kubeconfig = "apiVersion: v1\nkind: Config\ncurrent-context: http-routing\n";
        _proxy = new ContainerBuilder("mcr.microsoft.com/dotnet/aspnet:10.0")
            .WithNetwork(_network)
            .WithExtraHost("host.docker.internal", "host-gateway")
            .WithPortBinding(8080, true)
            .WithResourceMapping(new DirectoryInfo(AppContext.BaseDirectory), "/app")
            .WithResourceMapping(Encoding.UTF8.GetBytes(kubeconfig), "/root/.kube/config")
            .WithEntrypoint("dotnet", "/app/dotnet-krp.dll")
            .WithCommand("--no-ui", "--no-discovery", "--routing", "hosts", "--forwarder", "hybrid", "--nameserver", "127.0.0.11")
            .WithEnvironment("Krp__HttpPort", "8080")
            .WithEnvironment("Krp__Http2Port", "0")
            .WithEnvironment("Krp__HttpsPort", "0")
            .WithEnvironment("Krp__HttpEndpoints__0__Host", "upstream")
            .WithEnvironment("Krp__HttpEndpoints__0__Path", "/api")
            .WithEnvironment("Krp__HttpEndpoints__0__LocalScheme", "http")
            .WithEnvironment("Krp__HttpEndpoints__0__LocalPort", _backend.GetMappedPublicPort(8080).ToString())
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080))
            .Build();
        await _proxy.StartAsync(timeout.Token);

        _client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            BaseAddress = new UriBuilder("http", _proxy.Hostname, _proxy.GetMappedPublicPort(8080)).Uri,
            Timeout = TimeSpan.FromSeconds(10),
        };
        _client.DefaultRequestHeaders.Host = "upstream:8080";
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _client?.Dispose();
        foreach (var container in new[] { _proxy, _backend, _upstream })
        {
            if (container != null)
            {
                if (TestContext.CurrentTestOutcome != UnitTestOutcome.Passed && container.State == TestcontainersStates.Running)
                {
                    var logs = await container.GetLogsAsync();
                    TestContext.WriteLine($"{container.Name}\n{logs.Stdout}\n{logs.Stderr}");
                }
                await container.DisposeAsync();
            }
        }
        if (_network != null)
        {
            await _network.DisposeAsync();
        }
    }

    [TestMethod]
    [DataRow("/api/item?value=1", "backend:/item?value=1")]
    [DataRow("/api", "backend:/")]
    public async Task HandleRequest_ShouldForwardToHostBackend_WhenConfiguredPortIsListening(string path, string expected)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        // Act
        using var response = await _client.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public async Task HandleRequest_ShouldUseOriginalUpstream_WhenHostBackendStops()
    {
        // Arrange
        Assert.AreEqual("backend:/item?value=1", await _client.GetStringAsync("/api/item?value=1"));

        // Act
        await _backend.StopAsync();

        // PortChecker caches availability for 500 ms. Keep the proxy listening while the backend is down.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string result;
        do
        {
            await Task.Delay(100, timeout.Token);
            using var response = await _client.GetAsync("/api/item?value=1", timeout.Token);
            result = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        while (result != "upstream:/api/item?value=1" && !timeout.IsCancellationRequested);

        // Assert
        Assert.AreEqual("upstream:/api/item?value=1", result);
    }

    [TestMethod]
    public async Task HandleRequest_ShouldUseOriginalUpstream_WhenPathDoesNotMatch()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api-other/item?value=1");

        // Act
        using var response = await _client.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("upstream:/api-other/item?value=1", result);
    }

    private IContainer CreateServer(string name)
    {
        var configuration = $$"""
            server {
                listen 8080;
                location / { return 200 "{{name}}:$request_uri"; }
            }
            """;
        return new ContainerBuilder("nginx:1.28-alpine")
            .WithNetwork(_network)
            .WithNetworkAliases(name)
            .WithPortBinding(8080, true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(configuration), "/etc/nginx/conf.d/default.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8080)))
            .Build();
    }
}
