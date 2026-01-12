using AutoFixture;
using Krp.Endpoints;
using Krp.Endpoints.HttpProxy;
using Krp.Endpoints.Models;
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
        services.AddTransient<HttpProxyEndpointHandler>();
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
}
