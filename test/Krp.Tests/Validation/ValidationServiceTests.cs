using Krp.DependencyInjection;
using Krp.Dns;
using Krp.Endpoints;
using Krp.Endpoints.HttpProxy;
using Krp.Endpoints.PortForward;
using Krp.Https;
using Krp.Kubernetes;
using Krp.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.IO;
using System.Threading;

namespace Krp.Tests.Validation;

[TestClass]
public sealed class ValidationServiceTests : TestBase
{
    private ValidationService Sut => Fixture.Freeze<ValidationService>();

    [TestMethod]
    public async Task StartAsync_WhenRoutingValidationFails_ShouldMarkStateFailed()
    {
        // Arrange
        Fixture.Customize<ValidationService>(composer => composer.OmitAutoProperties());

        var hostsPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.hosts");
        var dnsOptions = Options.Create(new DnsHostsOptions { Path = hostsPath });
        var dnsHandler = new DnsHostsHandler(dnsOptions, Mock.Of<ILogger<DnsHostsHandler>>());

        Fixture.Inject(CreateEndpointManager());
        Fixture.Inject(new KubernetesClient(Mock.Of<ILogger<KubernetesClient>>()));
        Fixture.Inject(Mock.Of<ICertificateManager>());
        Fixture.Inject<IDnsHandler>(dnsHandler);
        Fixture.Inject(Mock.Of<ILogger<ValidationService>>());
        Fixture.Inject<IOptions<DnsHostsOptions>>(dnsOptions);
        var validationState = Fixture.Freeze<ValidationState>();
        Fixture.Inject(validationState);
        Fixture.Inject<IOptions<ValidationOptions>>(Options.Create(new ValidationOptions { ExitOnFailure = false }));

        // Act
        await Sut.StartAsync(CancellationToken.None);

        // Assert
        Assert.IsTrue(validationState.IsCompleted);
        Assert.IsFalse(validationState.Succeeded);
    }

    private static EndpointManager CreateEndpointManager()
    {
        var services = new ServiceCollection();
        services.AddTransient<HttpProxyEndpointHandler>();
        services.AddTransient<PortForwardEndpointHandler>();

        var provider = services.BuildServiceProvider();
        return new EndpointManager(provider, Mock.Of<ILogger<EndpointManager>>(), Options.Create(new KubernetesForwarderOptions()));
    }
}
