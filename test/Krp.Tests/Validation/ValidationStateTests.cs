using Krp.Validation;
using System.Threading;

namespace Krp.Tests.Validation;

[TestClass]
public sealed class ValidationStateTests : TestBase
{
    private ValidationState Sut => Fixture.Freeze<ValidationState>();

    [TestMethod]
    public async Task WaitForCompletionAsync_ReturnsTrueWhenSucceeded()
    {
        // Arrange
        Sut.MarkCompleted(true);

        // Act
        var result = await Sut.WaitForCompletionAsync(CancellationToken.None);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task WaitForCompletionAsync_ReturnsFalseWhenFailed()
    {
        // Arrange
        Sut.MarkCompleted(false);

        // Act
        var result = await Sut.WaitForCompletionAsync(CancellationToken.None);

        // Assert
        Assert.IsFalse(result);
    }
}
