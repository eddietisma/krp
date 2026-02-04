using Krp.ContextSwitching;
using Krp.DependencyInjection;
using Krp.Kubernetes;
using Krp.Validation;
using Krp.Tests.Assertions;
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

        var logger = Fixture.Freeze<Mock<ILogger<ContextSwitchingWatcher>>>();
        var validationState = new ValidationState();
        validationState.MarkCompleted(false);

        Fixture.Inject(Fixture.Freeze<Mock<IServiceProvider>>().Object);
        Fixture.Inject<IOptions<KubernetesForwarderOptions>>(Options.Create(new KubernetesForwarderOptions()));
        Fixture.Inject(Fixture.Create<EndpointManager>());
        Fixture.Inject(Fixture.Create<KubernetesClient>());
        Fixture.Inject(logger.Object);
        Fixture.Inject(validationState);

        // Act
        await Sut.RunAsync(CancellationToken.None);

        // Assert
        Assert.ShouldLog(logger, LogLevel.Warning, "Skipping context switch monitoring");
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
