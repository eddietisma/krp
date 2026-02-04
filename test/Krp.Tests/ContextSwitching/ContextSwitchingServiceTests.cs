using Krp.ContextSwitching;
using Krp.DependencyInjection;
using Krp.Kubernetes;
using Krp.Tests.Assertions;
using Krp.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Tests.ContextSwitching;

[TestClass]
public sealed class ContextSwitchingServiceTests : TestBase
{
    private ContextSwitchingService Sut => Fixture.Freeze<ContextSwitchingService>();

    [TestMethod]
    public async Task RunAsync_WhenValidationFailed_ShouldLogAndReturn()
    {
        // Arrange
        Fixture.Customize<ContextSwitchingService>(composer => composer.OmitAutoProperties());

        var logger = Fixture.Freeze<Mock<ILogger<ContextSwitchingService>>>();
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
}
