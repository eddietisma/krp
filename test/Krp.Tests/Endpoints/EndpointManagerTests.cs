using AutoFixture;
using Krp.Endpoints;
using Krp.Endpoints.HttpProxy;
using Krp.Endpoints.Models;
using Krp.Endpoints.PortForward;
using Microsoft.Extensions.DependencyInjection;

namespace Krp.Tests.Endpoints;

[TestClass]
public sealed class EndpointManagerTests : TestBase
{
    private EndpointManager Sut => Fixture.Freeze<EndpointManager>();

    [TestInitialize]
    public void Initialize()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<HttpProxyEndpointHandler>();
        services.AddSingleton<ProcessRunner>();
        services.AddTransient<PortForwardEndpointHandler>();
        var serviceProvider = services.BuildServiceProvider();
        
        Fixture.Inject<IServiceProvider>(serviceProvider);
    }

    [TestMethod]
    [DataRow("/", "/")]
    [DataRow("/service", "/service")]
    [DataRow("/service", "/service/resource")]
    [DataRow("/service/", "/service/resource")]
    [DataRow("service", "/service/resource")]
    public void GetHttpEndpointByUrl_MatchesConfiguredPaths(string configuredPath, string requestPath)
    {
        // Arrange
        var endpoint = Fixture.Build<HttpEndpoint>()
            .With(e => e.Host, "api.local")
            .With(e => e.Path, configuredPath)
            .With(e => e.LocalPort, 5000)
            .With(e => e.LocalScheme, "http")
            .Create();

        Sut.AddEndpoint(endpoint);

        // Act
        var handler = Sut.GetHttpEndpointByUrl("api.local", requestPath);

        // Assert
        Assert.IsNotNull(handler, $"Expected match for configured path '{configuredPath}' and request '{requestPath}'.");
    }

    [TestMethod]
    public void GetHttpEndpointByUrl_ReturnsNullForUnmatchedPath()
    {
        // Arrange
        var endpoint = Fixture.Build<HttpEndpoint>()
            .With(e => e.Host, "api.local")
            .With(e => e.Path, "/service")
            .With(e => e.LocalPort, 5000)
            .With(e => e.LocalScheme, "http")
            .Create();

        Sut.AddEndpoint(endpoint);

        // Act
        var handler = Sut.GetHttpEndpointByUrl("service.local", "/different");

        // Assert
        Assert.IsNull(handler);
    }

    [TestMethod]
    public void GetHttpEndpointByUrl_ReturnsNullWhenHostDoesNotMatch()
    {
        // Arrange
        var endpoint = Fixture.Build<HttpEndpoint>()
            .With(e => e.Host, "api.local")
            .With(e => e.Path, "/service")
            .With(e => e.LocalPort, 5000)
            .With(e => e.LocalScheme, "http")
            .Create();

        Sut.AddEndpoint(endpoint);

        // Act
        var handler = Sut.GetHttpEndpointByUrl("other.local", "/api");

        // Assert
        Assert.IsNull(handler);
    }

    [TestMethod]
    public void AddEndpoints_ShouldRegisterAllPorts_WhenServiceExposesMultiplePorts()
    {
        // Arrange
        var endpoints = new List<KubernetesEndpoint>
        {
            new()
            {
                Namespace = "legacyproxy",
                RemotePort = 8080,
                Resource = "service/legacyproxy-erpacc",
            },
            new()
            {
                Namespace = "legacyproxy",
                RemotePort = 8081,
                Resource = "service/legacyproxy-erpacc",
            },
        };

        // Act
        Sut.AddEndpoints(endpoints);

        // Assert
        var handlers = Sut.GetAllHandlers().OfType<PortForwardEndpointHandler>().ToList();
        Assert.AreEqual(2, handlers.Count);
        CollectionAssert.AreEquivalent(new[] { "legacyproxy-erpacc.legacyproxy:8080", "legacyproxy-erpacc.legacyproxy:8081" }, handlers.Select(x => x.Url).ToArray());
    }

    [TestMethod]
    public void AddEndpoints_ShouldReuseLocalIp_WhenServiceExposesMultiplePorts()
    {
        // Arrange
        var endpoints = new List<KubernetesEndpoint>
        {
            new()
            {
                Namespace = "legacyproxy",
                RemotePort = 8080,
                Resource = "service/legacyproxy-erpacc",
            },
            new()
            {
                Namespace = "legacyproxy",
                RemotePort = 8081,
                Resource = "service/legacyproxy-erpacc",
            },
        };

        // Act
        Sut.AddEndpoints(endpoints);

        // Assert
        var ips = Sut.GetAllHandlers().OfType<PortForwardEndpointHandler>().Select(x => x.LocalIp).Distinct().ToList();
        Assert.AreEqual(1, ips.Count);
    }

    [TestMethod]
    public void GetPortForwardHandlerByHost_ShouldReturnMatchingPort_WhenServiceExposesMultiplePorts()
    {
        // Arrange
        Sut.AddEndpoints([
            new KubernetesEndpoint
            {
                Namespace = "legacyproxy",
                RemotePort = 8080,
                Resource = "service/legacyproxy-erpacc",
            },
            new KubernetesEndpoint
            {
                Namespace = "legacyproxy",
                RemotePort = 8081,
                Resource = "service/legacyproxy-erpacc",
            },
        ]);

        // Act
        var handler = Sut.GetPortForwardHandlerByHost("legacyproxy-erpacc.legacyproxy", 8081);

        // Assert
        Assert.IsInstanceOfType<PortForwardEndpointHandler>(handler);
        Assert.AreEqual(8081, ((PortForwardEndpointHandler)handler).RemotePort);
    }

    [TestMethod]
    public void GetHandlerByIpPort_ShouldReturnMatchingPort_WhenServiceExposesMultiplePorts()
    {
        // Arrange
        Sut.AddEndpoints([
            new KubernetesEndpoint
            {
                Namespace = "legacyproxy",
                RemotePort = 8080,
                Resource = "service/legacyproxy-erpacc",
            },
            new KubernetesEndpoint
            {
                Namespace = "legacyproxy",
                RemotePort = 8081,
                Resource = "service/legacyproxy-erpacc",
            },
        ]);

        var ip = Sut.GetAllHandlers().OfType<PortForwardEndpointHandler>().First().LocalIp;

        // Act
        var handler = Sut.GetHandlerByIpPort(ip, 8081);

        // Assert
        Assert.IsInstanceOfType<PortForwardEndpointHandler>(handler);
        Assert.AreEqual(8081, ((PortForwardEndpointHandler)handler).RemotePort);
    }

    [TestMethod]
    public void AddEndpoint_ShouldUseConfiguredHost_WhenResourceIsPod()
    {
        // Arrange
        var endpoint = new KubernetesEndpoint
        {
            Host = "legacyproxy-erpacc.legacyproxy",
            Namespace = "legacyproxy",
            RemotePort = 8081,
            Resource = "pod/legacyproxy-erpacc-77fcccc5bf-qf6wx",
        };

        // Act
        Sut.AddEndpoint(endpoint);

        // Assert
        var handler = Sut.GetAllHandlers().OfType<PortForwardEndpointHandler>().Single();
        Assert.AreEqual("legacyproxy-erpacc.legacyproxy", handler.Host);
        Assert.AreEqual("legacyproxy-erpacc.legacyproxy:8081", handler.Url);
    }

    [TestMethod]
    public void AddEndpoints_ShouldReuseLocalIp_WhenServiceAndPodShareHost()
    {
        // Arrange
        Sut.AddEndpoints([
            new KubernetesEndpoint
            {
                Host = "legacyproxy-erpacc.legacyproxy",
                Namespace = "legacyproxy",
                RemotePort = 8080,
                Resource = "service/legacyproxy-erpacc",
            },
            new KubernetesEndpoint
            {
                Host = "legacyproxy-erpacc.legacyproxy",
                Namespace = "legacyproxy",
                RemotePort = 8081,
                Resource = "pod/legacyproxy-erpacc-77fcccc5bf-qf6wx",
            },
        ]);

        // Act
        var handlers = Sut.GetAllHandlers().OfType<PortForwardEndpointHandler>().ToList();

        // Assert
        Assert.AreEqual(2, handlers.Count);
        Assert.AreEqual(1, handlers.Select(x => x.LocalIp).Distinct().Count());
    }
}
