using AutoFixture;
using Krp.Https;
using Krp.Tool.Commands;
using McMaster.Extensions.CommandLineUtils;
using Moq;

namespace Krp.Tests.Commands;

[TestClass]
[DoNotParallelize]
public sealed class HttpsCommandTests : TestBase
{
    private HttpsCommand Sut => Fixture.Freeze<HttpsCommand>();

    private TextWriter _originalError;
    private StringWriter _errorWriter;

    [TestInitialize]
    public void Initialize()
    {
        Fixture.Customize<HttpsCommand>(composer => composer.OmitAutoProperties());

        _originalError = Console.Error;
        _errorWriter = new StringWriter();
        Console.SetError(_errorWriter);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Console.SetError(_originalError);
        _errorWriter?.Dispose();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldCallTryCheckCertificateAuthority_WhenCheckOnly()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        certificateManagerMock
            .Setup(x => x.TryCheckCertificateAuthority(out It.Ref<string>.IsAny))
            .Returns(true);

        Sut.Check = true;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(0, exitCode);
        certificateManagerMock.Verify(x => x.TryCheckCertificateAuthority(out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldCallTryCheckTrustedCertificateAuthority_WhenCheckAndTrust()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        certificateManagerMock
            .Setup(x => x.TryCheckTrustedCertificateAuthority(out It.Ref<string>.IsAny))
            .Returns(true);

        Sut.Check = true;
        Sut.Trust = true;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(0, exitCode);
        certificateManagerMock.Verify(x => x.TryCheckTrustedCertificateAuthority(out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldCallTryCleanCertificateAuthority_WhenCleanOnly()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        certificateManagerMock
            .Setup(x => x.TryCleanCertificateAuthority(out It.Ref<string>.IsAny))
            .Returns(true);

        Sut.Clean = true;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(0, exitCode);
        certificateManagerMock.Verify(x => x.TryCleanCertificateAuthority(out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldCallTryCreateCertificateAuthority_WhenNoOptions()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        certificateManagerMock
            .Setup(x => x.TryCreateCertificateAuthority(false, out It.Ref<string>.IsAny))
            .Returns(true);

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(0, exitCode);
        certificateManagerMock.Verify(x => x.TryCreateCertificateAuthority(false, out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldCallTryCreateCertificateAuthority_WhenTrustOnly()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        certificateManagerMock
            .Setup(x => x.TryCreateCertificateAuthority(true, out It.Ref<string>.IsAny))
            .Returns(true);

        Sut.Trust = true;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(0, exitCode);
        certificateManagerMock.Verify(x => x.TryCreateCertificateAuthority(true, out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldCallTryExportCertificateAuthority_WhenExportPem()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        certificateManagerMock
            .Setup(x => x.TryExportCertificateAuthority("out.pem", "", CertificateExportFormat.Pem, out It.Ref<string>.IsAny))
            .Returns(true);

        Sut.ExportPath = "out.pem";
        Sut.ExportFormat = CertificateExportFormat.Pem;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(0, exitCode);
        certificateManagerMock.Verify(x => x.TryExportCertificateAuthority("out.pem", "", CertificateExportFormat.Pem, out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldCallTryExportCertificateAuthority_WhenExportPfx()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        certificateManagerMock
            .Setup(x => x.TryExportCertificateAuthority("out.pfx", "pw", CertificateExportFormat.Pfx, out It.Ref<string>.IsAny))
            .Returns(true);

        Sut.ExportPath = "out.pfx";
        Sut.ExportFormat = CertificateExportFormat.Pfx;
        Sut.Password = "pw";

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(0, exitCode);
        certificateManagerMock.Verify(x => x.TryExportCertificateAuthority("out.pfx", "pw", CertificateExportFormat.Pfx, out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldCallTryImportCertificateAuthority_WhenImportWithTrust()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        certificateManagerMock
            .Setup(x => x.TryImportCertificateAuthority("in.pfx", "pw", true, out It.Ref<string>.IsAny))
            .Returns(true);

        Sut.ImportPath = "in.pfx";
        Sut.Password = "pw";
        Sut.Trust = true;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(0, exitCode);
        certificateManagerMock.Verify(x => x.TryImportCertificateAuthority("in.pfx", "pw", true, out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldCallTryImportCertificateAuthority_WhenImportWithoutTrust()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        certificateManagerMock
            .Setup(x => x.TryImportCertificateAuthority("in.pfx", "pw", false, out It.Ref<string>.IsAny))
            .Returns(true);

        Sut.ImportPath = "in.pfx";
        Sut.Password = "pw";

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(0, exitCode);
        certificateManagerMock.Verify(x => x.TryImportCertificateAuthority("in.pfx", "pw", false, out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReject_WhenCheckAndExportSpecified()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();

        Sut.Check = true;
        Sut.ExportPath = "out.pfx";

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("--check", _errorWriter.ToString());
        Assert.Contains("--export", _errorWriter.ToString());
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReject_WhenCheckAndImportSpecified()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        Sut.Check = true;
        Sut.ImportPath = "in.pfx";

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("--check", _errorWriter.ToString());
        Assert.Contains("--import", _errorWriter.ToString());
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReject_WhenCleanAndCheckSpecified()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        Sut.Clean = true;
        Sut.Check = true;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("--clean", _errorWriter.ToString());
        Assert.Contains("--check", _errorWriter.ToString());
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReject_WhenCleanAndExportSpecified()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        Sut.Clean = true;
        Sut.ExportPath = "out.pfx";

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("--clean", _errorWriter.ToString());
        Assert.Contains("--export", _errorWriter.ToString());
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReject_WhenCleanAndImportSpecified()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        Sut.Clean = true;
        Sut.ImportPath = "in.pfx";

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("--clean", _errorWriter.ToString());
        Assert.Contains("--import", _errorWriter.ToString());
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReject_WhenCleanAndTrustSpecified()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        Sut.Clean = true;
        Sut.Trust = true;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("--clean", _errorWriter.ToString());
        Assert.Contains("--trust", _errorWriter.ToString());
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReject_WhenExportPfxWithoutPassword()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        Sut.ExportPath = "out.pfx";
        Sut.ExportFormat = CertificateExportFormat.Pfx;
        Sut.Password = "";

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("PFX export requires a password", _errorWriter.ToString());
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReject_WhenImportAndExportSpecified()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        Sut.ImportPath = "in.pfx";
        Sut.ExportPath = "out.pfx";

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("--import", _errorWriter.ToString());
        Assert.Contains("--export", _errorWriter.ToString());
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReject_WhenTrustAndExportSpecified()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        Sut.Trust = true;
        Sut.ExportPath = "out.pfx";

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("--trust", _errorWriter.ToString());
        Assert.Contains("--export", _errorWriter.ToString());
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReturn1AndWriteError_WhenCheckFails()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        var error = "check-failed";
        certificateManagerMock
            .Setup(x => x.TryCheckCertificateAuthority(out error))
            .Returns(false);

        Sut.Check = true;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("check-failed", _errorWriter.ToString());
        certificateManagerMock.Verify(x => x.TryCheckCertificateAuthority(out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReturn1AndWriteError_WhenCleanFails()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        var error = "clean-failed";
        certificateManagerMock
            .Setup(x => x.TryCleanCertificateAuthority(out error))
            .Returns(false);

        Sut.Clean = true;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("clean-failed", _errorWriter.ToString());
        certificateManagerMock.Verify(x => x.TryCleanCertificateAuthority(out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReturn1AndWriteError_WhenCreateWithoutTrustFails()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        var error = "create-without-trust-failed";
        certificateManagerMock
            .Setup(x => x.TryCreateCertificateAuthority(false, out error))
            .Returns(false);

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("create-without-trust-failed", _errorWriter.ToString());
        certificateManagerMock.Verify(x => x.TryCreateCertificateAuthority(false, out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReturn1AndWriteError_WhenExportFails()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        var error = "export-failed";
        certificateManagerMock
            .Setup(x => x.TryExportCertificateAuthority("out.pfx", "pw", CertificateExportFormat.Pfx, out error))
            .Returns(false);

        Sut.ExportPath = "out.pfx";
        Sut.ExportFormat = CertificateExportFormat.Pfx;
        Sut.Password = "pw";

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("export-failed", _errorWriter.ToString());
        certificateManagerMock.Verify(x => x.TryExportCertificateAuthority("out.pfx", "pw", CertificateExportFormat.Pfx, out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReturn1AndWriteError_WhenImportFails()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        var error = "import-failed";
        certificateManagerMock
            .Setup(x => x.TryImportCertificateAuthority("in.pfx", "pw", false, out error))
            .Returns(false);

        Sut.ImportPath = "in.pfx";
        Sut.Password = "pw";

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("import-failed", _errorWriter.ToString());
        certificateManagerMock.Verify(x => x.TryImportCertificateAuthority("in.pfx", "pw", false, out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReturn1AndWriteError_WhenTrustCreateFails()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        var error = "trust-create-failed";
        certificateManagerMock
            .Setup(x => x.TryCreateCertificateAuthority(true, out error))
            .Returns(false);

        Sut.Trust = true;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("trust-create-failed", _errorWriter.ToString());
        certificateManagerMock.Verify(x => x.TryCreateCertificateAuthority(true, out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task OnExecuteAsync_ShouldReturn1AndWriteError_WhenTrustedCheckFails()
    {
        // Arrange
        var certificateManagerMock = Fixture.Freeze<Mock<ICertificateManager>>();
        var error = "trusted-check-failed";
        certificateManagerMock
            .Setup(x => x.TryCheckTrustedCertificateAuthority(out error))
            .Returns(false);

        Sut.Check = true;
        Sut.Trust = true;

        // Act
        var exitCode = await Sut.OnExecuteAsync(new CommandLineApplication());

        // Assert
        Assert.AreEqual(1, exitCode);
        Assert.Contains("trusted-check-failed", _errorWriter.ToString());
        certificateManagerMock.Verify(x => x.TryCheckTrustedCertificateAuthority(out It.Ref<string>.IsAny), Times.Once);
        certificateManagerMock.VerifyNoOtherCalls();
    }
}
