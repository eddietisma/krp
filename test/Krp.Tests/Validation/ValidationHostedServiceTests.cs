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
        var kubeConfigPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.config");

        Fixture.Freeze<Mock<IKubernetesClient>>()
            .Setup(x => x.TryGetKubeConfigPath(out kubeConfigPath))
            .Returns(true);
        
        // Act
        await Sut.StartAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // Give enough time when debugging.
        await validationState.WaitForCompletionAsync(cts.Token);

        // Assert
        Assert.IsTrue(validationState.IsCompleted);
        Assert.IsTrue(validationState.IsValid);
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

        var kubeConfigPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.config");
        Fixture.Freeze<Mock<IKubernetesClient>>()
            .Setup(x => x.TryGetKubeConfigPath(out kubeConfigPath))
            .Returns(true);

        // Act
        await Sut.StartAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // Give enough time when debugging.
        await validationState.WaitForCompletionAsync(cts.Token);

        // Assert
        Assert.IsTrue(validationState.IsCompleted);
        Assert.IsFalse(validationState.IsValid);
    }
}
