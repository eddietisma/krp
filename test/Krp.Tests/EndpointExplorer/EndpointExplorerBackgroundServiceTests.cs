using Krp.DependencyInjection;
using Krp.EndpointExplorer;
using Krp.Endpoints;
using Krp.Endpoints.HttpProxy;
using Krp.Endpoints.PortForward;
using Krp.Kubernetes;
using Krp.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Tests.EndpointExplorer;

[TestClass]
public sealed class EndpointExplorerBackgroundServiceTests : TestBase
{
    private TestableEndpointExplorerBackgroundService Sut => Fixture.Freeze<TestableEndpointExplorerBackgroundService>();

    [TestMethod]
    public async Task ExecuteAsync_WhenValidationFailed_ShouldLogAndReturn()
    {
        // Arrange
        Fixture.Customize<TestableEndpointExplorerBackgroundService>(composer => composer.OmitAutoProperties());

        var logger = new Mock<ILogger<EndpointExplorerBackgroundService>>();
        var validationState = new ValidationState();
        validationState.MarkCompleted(false);

        Fixture.Inject(CreateEndpointExplorer());
        Fixture.Inject<IOptions<EndpointExplorerOptions>>(Options.Create(new EndpointExplorerOptions
        {
            RefreshInterval = TimeSpan.FromMinutes(1),
        }));
        Fixture.Inject(logger.Object);
        Fixture.Inject(validationState);

        // Act
        await Sut.RunAsync(CancellationToken.None);

        // Assert
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Skipping endpoint discovery", StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static EndpointExplorer.EndpointExplorer CreateEndpointExplorer()
    {
        var services = new ServiceCollection();
        services.AddTransient<HttpProxyEndpointHandler>();
        services.AddTransient<PortForwardEndpointHandler>();

        var provider = services.BuildServiceProvider();
        var endpointManager = new EndpointManager(provider, Mock.Of<ILogger<EndpointManager>>(), Options.Create(new KubernetesForwarderOptions()));
        var kubernetesClient = new KubernetesClient(Mock.Of<ILogger<KubernetesClient>>());

        return new EndpointExplorer.EndpointExplorer(
            endpointManager,
            kubernetesClient,
            Options.Create(new EndpointExplorerOptions()),
            Mock.Of<ILogger<EndpointExplorer.EndpointExplorer>>());
    }

    private sealed class TestableEndpointExplorerBackgroundService : EndpointExplorerBackgroundService
    {
        public TestableEndpointExplorerBackgroundService(
            EndpointExplorer.EndpointExplorer explorer,
            IOptions<EndpointExplorerOptions> options,
            ILogger<EndpointExplorerBackgroundService> logger,
            ValidationState validationState)
            : base(explorer, options, logger, validationState)
        {
        }

        public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
    }
}
