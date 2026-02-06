using AutoFixture;
using Krp.EndpointExplorer;
using Krp.Tests.Assertions;
using Krp.Validation;
using Microsoft.Extensions.Logging;
using Moq;

namespace Krp.Tests.EndpointExplorer;

[TestClass]
public sealed class EndpointExplorerBackgroundServiceTests : TestBase
{
    private EndpointExplorerBackgroundService Sut => Fixture.Freeze<EndpointExplorerBackgroundService>();

    [TestMethod]
    public async Task StartAsync_ShouldLogAndReturn_WhenValidationFailed()
    {
        // Arrange
        var logger = Fixture.Freeze<Mock<ILogger<EndpointExplorerBackgroundService>>>();
        var validationState = new ValidationState();
        validationState.MarkCompleted(false);
        Fixture.Inject(validationState);

        // Act
        await Sut.StartAsync(CancellationToken.None);

        // Assert
        LogAssert.ShouldLog(logger, LogLevel.Warning, "Skipping endpoint discovery because validation failed");
    }
}
