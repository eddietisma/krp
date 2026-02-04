using Krp.ContextSwitching;
using Krp.DependencyInjection;
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

namespace Krp.Tests.ContextSwitching;

[TestClass]
public sealed class ContextSwitchingWatcherTests : TestBase
{
    private TestableContextSwitchingWatcher Sut => Fixture.Freeze<TestableContextSwitchingWatcher>();

    [TestMethod]
    public async Task ExecuteAsync_WhenValidationFailed_ShouldLogAndReturn()
    {
        // Arrange
        Fixture.Customize<TestableContextSwitchingWatcher>(composer => composer.OmitAutoProperties());

        var logger = new Mock<ILogger<ContextSwitchingWatcher>>();
        var validationState = new ValidationState();
        validationState.MarkCompleted(false);

        Fixture.Inject(CreateServiceProvider());
        Fixture.Inject(CreateEndpointManager());
        Fixture.Inject(new KubernetesClient(Mock.Of<ILogger<KubernetesClient>>()));
        Fixture.Inject(logger.Object);
        Fixture.Inject(validationState);

        // Act
        await Sut.RunAsync(CancellationToken.None);

        // Assert
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Skipping context switch monitoring", StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        return services.BuildServiceProvider();
    }

    private static EndpointManager CreateEndpointManager()
    {
        var services = new ServiceCollection();
        services.AddTransient<HttpProxyEndpointHandler>();
        services.AddTransient<PortForwardEndpointHandler>();

        var provider = services.BuildServiceProvider();
        return new EndpointManager(provider, Mock.Of<ILogger<EndpointManager>>(), Options.Create(new KubernetesForwarderOptions()));
    }

    private sealed class TestableContextSwitchingWatcher : ContextSwitchingWatcher
    {
        public TestableContextSwitchingWatcher(
            IServiceProvider serviceProvider,
            EndpointManager endpointManager,
            KubernetesClient kbKubernetesClient,
            ILogger<ContextSwitchingWatcher> logger,
            ValidationState validationState)
            : base(serviceProvider, endpointManager, kbKubernetesClient, logger, validationState)
        {
        }

        public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
    }
}
