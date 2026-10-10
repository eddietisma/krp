using AutoFixture;
using Krp.ContextSwitching;
using Krp.Tests.Assertions;
using Krp.Validation;
using Microsoft.Extensions.Logging;
using Moq;

namespace Krp.Tests.ContextSwitching;

[TestClass]
public sealed class ContextSwitchingBackgroundServiceTests : TestBase
{
    private ContextSwitchingBackgroundService Sut => Fixture.Freeze<ContextSwitchingBackgroundService>();

    [TestMethod]
    public async Task StartAsync_ShouldLogAndReturn_WhenValidationFailed()
    {
        // Arrange
        var logger = Fixture.Freeze<Mock<ILogger<ContextSwitchingBackgroundService>>>();
        var validationState = new ValidationState();
        validationState.MarkCompleted(false);
        Fixture.Inject(validationState);

        // Act
        await Sut.StartAsync(CancellationToken.None);

        // Assert
        LogAssert.ShouldLog(logger, LogLevel.Warning, "Skipping context switch monitoring because validation failed");
    }
}
