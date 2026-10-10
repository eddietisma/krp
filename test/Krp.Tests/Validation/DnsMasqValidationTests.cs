using AutoFixture;
using Krp.Dns;
using Krp.Endpoints;
using Krp.Kubernetes;
using Krp.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Krp.Tests.Validation;

[TestClass]
public sealed class DnsMasqValidationTests : TestBase
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task StartAsync_ShouldAcceptConfiguration_WhenDirectoryIsWritable(bool directoryExists)
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), $"krp-dnsmasq-{Guid.NewGuid():N}");
        if (directoryExists)
        {
            Directory.CreateDirectory(directory);
        }

        var options = new DnsMasqOptions
        {
            OverridePath = Path.Combine(directory, "krp.override.conf"),
            RestartCommand = "dnsmasq-restart",
        };

        try
        {
            // Act
            var state = await ValidateAsync(options);

            // Assert
            Assert.IsTrue(state.IsValid);
            Assert.IsTrue(Directory.Exists(directory));
            Assert.AreEqual(0, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task StartAsync_ShouldRejectConfiguration_WhenRestartCommandIsMissing()
    {
        // Arrange
        var options = new DnsMasqOptions();

        // Act
        var state = await ValidateAsync(options);

        // Assert
        Assert.IsFalse(state.IsValid);
        Fixture.Freeze<Mock<IEndpointManager>>().Verify(manager => manager.Initialize(), Times.Never);
    }

    [TestMethod]
    public async Task StartAsync_ShouldRejectConfiguration_WhenParentPathIsAFile()
    {
        // Arrange
        var path = Path.GetTempFileName();
        var options = new DnsMasqOptions
        {
            OverridePath = Path.Combine(path, "krp.override.conf"),
            RestartCommand = "dnsmasq-restart",
        };

        try
        {
            // Act
            var state = await ValidateAsync(options);

            // Assert
            Assert.IsFalse(state.IsValid);
            Assert.IsTrue(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private async Task<ValidationState> ValidateAsync(DnsMasqOptions options)
    {
        var dnsOptions = Options.Create(options);
        Fixture.Inject(dnsOptions);
        Fixture.Inject(Options.Create(new ValidationOptions { ExitOnFailure = false }));
        Fixture.Inject<IDnsHandler>(new DnsMasqHandler(dnsOptions, NullLogger<DnsMasqHandler>.Instance));
        var kubeConfigPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.config");
        Fixture.Freeze<Mock<IKubernetesClient>>()
            .Setup(client => client.TryGetKubeConfigPath(out kubeConfigPath))
            .Returns(true);

        var state = Fixture.Freeze<ValidationState>();
        var service = Fixture.Create<ValidationHostedService>();
        await service.StartAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await state.WaitForCompletionAsync(timeout.Token);
        await service.StopAsync(CancellationToken.None);
        return state;
    }
}
