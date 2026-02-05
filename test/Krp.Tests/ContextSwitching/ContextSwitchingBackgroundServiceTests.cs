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
    public async Task RunAsync_WhenValidationFailed_ShouldLogAndReturn()
    {
        // Arrange
        var logger = Fixture.Freeze<Mock<ILogger<ContextSwitchingBackgroundService>>>();
        var validationState = new ValidationState();
        validationState.MarkCompleted(false);

        // Act
        await Sut.StartAsync(CancellationToken.None);

        // Assert
        LogAssert.ShouldLog(logger, LogLevel.Warning, "Skipping context switch monitoring");
    }
}
