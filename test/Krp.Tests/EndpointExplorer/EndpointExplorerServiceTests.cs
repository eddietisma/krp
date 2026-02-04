using Krp.DependencyInjection;
using Krp.EndpointExplorer;
using Krp.Kubernetes;
using Krp.Tests.Assertions;
using Krp.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Tests.EndpointExplorer;

[TestClass]
public sealed class EndpointExplorerServiceTests : TestBase
{
    private EndpointExplorerService Sut => Fixture.Freeze<EndpointExplorerService>();

    [TestMethod]
    public async Task RunAsync_WhenValidationFailed_ShouldLogAndReturn()
    {
        // Arrange
        Fixture.Customize<EndpointExplorerService>(composer => composer.OmitAutoProperties());

        var logger = Fixture.Freeze<Mock<ILogger<EndpointExplorerService>>>();
        var validationState = new ValidationState();
        validationState.MarkCompleted(false);

        Fixture.Inject<IOptions<KubernetesForwarderOptions>>(Options.Create(new KubernetesForwarderOptions()));
        Fixture.Inject(Fixture.Create<EndpointExplorer.EndpointExplorer>());
        Fixture.Inject<IOptions<EndpointExplorerOptions>>(Options.Create(new EndpointExplorerOptions
        {
            RefreshInterval = TimeSpan.FromMinutes(1),
        }));
        Fixture.Inject(logger.Object);
        Fixture.Inject(validationState);

        // Act
        await Sut.RunAsync(CancellationToken.None);

        // Assert
        Assert.ShouldLog(logger, LogLevel.Warning, "Skipping endpoint discovery");
    }
}
