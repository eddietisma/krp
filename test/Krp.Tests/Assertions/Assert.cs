using Microsoft.Extensions.Logging;
using Moq;
using System;

namespace Krp.Tests.Assertions;

public static class Assert
{
    public static void ShouldLog<T>(Mock<ILogger<T>> logger, string messageContains)
    {
        ShouldLog(logger, LogLevel.Warning, messageContains);
    }

    public static void ShouldLog<T>(Mock<ILogger<T>> logger, LogLevel level, string messageContains)
    {
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(messageContains, StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
