using AutoFixture;
using Krp.Dns;
using Krp.Kubernetes;
using Krp.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Krp.Tests.Validation;

[TestClass]
public sealed class ValidationHostedServiceTests : TestBase
{
    private ValidationHostedService Sut => Fixture.Freeze<ValidationHostedService>();

    [TestMethod]
    public async Task StartAsync_ShouldMarkValidationState()
    {
        // Arrange
        var validationState = Fixture.Freeze<ValidationState>();

        Fixture.Freeze<Mock<IKubernetesClient>>()
            .Setup(x => x.WaitForAccess(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns(true);
        
        // Act
        await Sut.StartAsync(CancellationToken.None);

        // Assert
        Assert.IsTrue(validationState.IsCompleted);
        Assert.IsTrue(validationState.Succeeded);
    }

    [TestMethod]
    public async Task StartAsync_ShouldMarkValidationState_WhenRoutingValidationFails()
    {
        // Arrange
        var nonExistingHostsFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.hosts");
        var dnsHostsOptions = Options.Create(new DnsHostsOptions { Path = nonExistingHostsFile });
        Fixture.Inject(dnsHostsOptions);
        Fixture.Inject<IDnsHandler>(new DnsHostsHandler(dnsHostsOptions, new NullLogger<DnsHostsHandler>()));

        var validationState = Fixture.Freeze<ValidationState>();
        Fixture.Inject(validationState);

        // Act
        await Sut.StartAsync(CancellationToken.None);

        // Assert
        Assert.IsTrue(validationState.IsCompleted);
        Assert.IsFalse(validationState.Succeeded);
    }
}
