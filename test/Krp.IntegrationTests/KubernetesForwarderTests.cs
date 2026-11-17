using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System;

namespace Krp.IntegrationTests;

[TestClass]
[TestCategory("K3s")]
public sealed class KubernetesForwarderTests
{
    private static K3sFixture _fixture;

    private KrpTestProcess Sut { get; set; }

    [ClassInitialize]
    public static async Task Initialize(TestContext context)
    {
        _fixture = new K3sFixture();
        try
        {
            await _fixture.InitializeAsync();
        }
        catch
        {
            await _fixture.DisposeAsync();
            _fixture = null;
            throw;
        }
    }

    [ClassCleanup]
    public static async Task CleanupFixture()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [TestInitialize]
    public void Setup()
    {
        Sut = new KrpTestProcess(_fixture);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await Sut.DisposeAsync();
    }

    [TestMethod]
    [DataRow("hybrid")]
    [DataRow("http")]
    [DataRow("tcp")]
    public async Task HandleRequest_ShouldForwardPathsAndQueries_WhenServicesAreDiscovered(string mode)
    {
        // Arrange
        await Sut.StartAsync(mode);
        using var client = Sut.CreateClient();

        foreach (var app in new[] { "app-one", "app-two" })
        {
            // Act
            var index = await client.GetStringAsync(Sut.GetUrl(app));
            var probe = await client.GetStringAsync(Sut.GetUrl(app, $"/probe.txt?mode={mode}"));

            // Assert
            Assert.AreEqual($"{app}-index", index);
            Assert.AreEqual($"{app}-probe", probe);
            await Sut.WaitUntilAsync(async () =>
            {
                var logs = await _fixture.KubectlAsync("logs", $"deployment/{app}", "-n", Sut.GetNamespace(app));
                return logs.Contains($"GET /probe.txt?mode={mode} HTTP/1.1", StringComparison.Ordinal);
            });
        }
        await Sut.WaitUntilAsync(() => Task.FromResult(Sut.GetForwardedPorts().Length >= 2));
        Assert.HasCount(2, Sut.GetForwardedPorts(), Sut.Logs);
    }

    [TestMethod]
    public async Task HandleRequest_ShouldRouteEachRequest_WhenRequestsAreConcurrent()
    {
        // Arrange
        await Sut.StartAsync("hybrid");
        using var client = Sut.CreateClient();

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            var app = i % 2 == 0 ? "app-one" : "app-two";
            var body = await client.GetStringAsync(Sut.GetUrl(app, $"/probe.txt?request={i}"));
            return (App: app, Body: body);
        }));

        // Assert
        foreach (var response in responses)
        {
            Assert.AreEqual($"{response.App}-probe", response.Body);
        }
        await Sut.WaitUntilAsync(() => Task.FromResult(Sut.GetForwardedPorts().Length >= 2));
        Assert.HasCount(2, Sut.GetForwardedPorts(), Sut.Logs);
    }

    [TestMethod]
    public async Task EnsureRunningAsync_ShouldRestartPortForward_WhenPodIsReplaced()
    {
        // Arrange
        await Sut.StartAsync("hybrid");
        using var client = Sut.CreateClient();
        Assert.AreEqual("app-one-index", await client.GetStringAsync(Sut.GetUrl("app-one")));

        // Act
        await _fixture.KubectlAsync("delete", "pod", "-n", Sut.GetNamespace("app-one"), "-l", "app=app-one", "--wait=true", "--timeout=30s");
        await _fixture.KubectlAsync("rollout", "status", "deployment/app-one", "-n", Sut.GetNamespace("app-one"), "--timeout=30s");
        await Sut.WaitUntilAsync(async () =>
        {
            try
            {
                using var response = await client.GetAsync(Sut.GetUrl("app-one"));
                return response.StatusCode == HttpStatusCode.OK && await response.Content.ReadAsStringAsync() == "app-one-index";
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        });

        // Assert
        await Sut.WaitUntilAsync(() => Task.FromResult(Sut.GetForwardedPorts().Length >= 2 && Sut.Logs.Contains("Process exited: 'kubectl port-forward service/app-one", StringComparison.Ordinal)));
        Assert.Contains("Process exited: 'kubectl port-forward service/app-one", Sut.Logs);
        Assert.IsGreaterThanOrEqualTo(2, Sut.GetForwardedPorts().Length, Sut.Logs);
    }

    [TestMethod]
    public async Task RunCommandAsync_ShouldClosePortForwardListeners_WhenParentIsKilled()
    {
        // Arrange
        await Sut.StartAsync("hybrid");
        using var client = Sut.CreateClient();
        Assert.AreEqual("app-one-index", await client.GetStringAsync(Sut.GetUrl("app-one")));
        Assert.AreEqual("app-two-index", await client.GetStringAsync(Sut.GetUrl("app-two")));
        await Sut.WaitUntilAsync(() => Task.FromResult(Sut.GetForwardedPorts().Length >= 2));
        var ports = Sut.GetForwardedPorts();
        Assert.HasCount(2, ports);

        // Act
        await Sut.DisposeAsync();
        await Sut.WaitUntilAsync(() => Task.FromResult(ports.All(port => !IsListening(port))));

        // Assert
        foreach (var port in ports)
        {
            Assert.IsFalse(IsListening(port), $"Port-forward listener {port} survived KRP exit.");
        }
    }

    [TestMethod]
    [DataRow("hybrid")]
    [DataRow("http")]
    public async Task HandleRequest_ShouldForwardHttps_WhenCertificateAuthorityExists(string mode)
    {
        // Arrange
        await Sut.StartAsync(mode);
        using var ca = X509Certificate2.CreateFromPem(await File.ReadAllTextAsync(_fixture.CertificateAuthorityPath));
        using var client = Sut.CreateClient(ca);

        foreach (var app in new[] { "app-one", "app-two" })
        {
            // Act
            string body;
            try
            {
                body = await client.GetStringAsync(Sut.GetUrl(app, "/probe.txt?mode=https", https: true));
            }
            catch (HttpRequestException ex)
            {
                Assert.Fail($"HTTPS forwarding failed: {ex.Message}\n{Sut.Logs}");
                return;
            }

            // Assert
            Assert.AreEqual($"{app}-probe", body);
        }
    }

    private static bool IsListening(int port)
    {
        return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == port);
    }
}