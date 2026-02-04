using Krp.ContextSwitching;
using Krp.DependencyInjection;
using Krp.Endpoints;
using Krp.Endpoints.HttpProxy;
using Krp.Endpoints.PortForward;
using Krp.Kubernetes;
using Krp.Validation;
using Krp.Tests.Assertions;
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
        Assert.ShouldLog(logger, "Skipping context switch monitoring");
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
