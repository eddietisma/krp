using AutoFixture;
using Krp.Https;
using Microsoft.Extensions.Options;
using Moq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Krp.Tests.Https;

[TestClass]
public sealed class CertificateManagerTests : TestBase
{
    private CertificateManager Sut => Fixture.Freeze<CertificateManager>();

    [TestInitialize]
    public void TestInitialize()
    {
        Fixture.Freeze<Mock<ICertificateStore>>();
        Fixture.Freeze<CertificateOptions>(comp => comp
            .With(x => x.DefaultCertificateAuthoritySubject, "CN=krp_test")
            .With(x => x.DefaultHostName, "localhost"));

        var created = Sut.TryCreateCertificateAuthority(trust: false, out var createMessage);
        Assert.IsTrue(created, createMessage);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        Sut.TryCleanCertificateAuthority(out var _);
    }

    [TestMethod]
    public void GetOrCreateServerCertificate_ShouldReturnCertificate_WhenCertificateAuthorityExists()
    {
        // Act
        var certificate = Sut.GetOrCreateServerCertificate("localhost");

        // Assert
        Assert.IsNotNull(certificate);
        Assert.Contains("CN=localhost", certificate.Subject);
        Assert.IsTrue(certificate.HasPrivateKey);
    }

    [TestMethod]
    public void GetOrCreateServerCertificate_ShouldThrow_WhenCertificateAuthorityMissing()
    {
        var options = Fixture.Freeze<CertificateOptions>();
        options.DefaultCertificateAuthoritySubject = "CN=krp_test_missing";

        // Act
        Action Act = () =>
        {
            Sut.GetOrCreateServerCertificate("localhost_missing");
        };

        // Assert
        var exception = Assert.Throws<InvalidOperationException>(Act);
        Assert.AreEqual("No valid certificate found.", exception.Message);
    }

    [TestMethod]
    public void NormalizeHostName_ShouldDefaultToLocalhost_WhenNullOrWhitespace()
    {
        // Arrange
        string nullHost = null;
        var emptyHost = "";
        var whitespaceHost = "   ";

        // Act
        var normalizedNull = NormalizeHostName(nullHost);
        var normalizedEmpty = NormalizeHostName(emptyHost);
        var normalizedWhitespace = NormalizeHostName(whitespaceHost);

        // Assert
        Assert.AreEqual("localhost", normalizedNull);
        Assert.AreEqual("localhost", normalizedEmpty);
        Assert.AreEqual("localhost", normalizedWhitespace);
    }

    [TestMethod]
    public void NormalizeHostName_ShouldNotMangleBareIpv6()
    {
        // Arrange
        var bareLoopback = "::1";
        var bareAddress = "2001:db8::1";

        // Act
        var loopback = NormalizeHostName(bareLoopback);
        var address = NormalizeHostName(bareAddress);

        // Assert
        Assert.AreEqual("::1", loopback);
        Assert.AreEqual("2001:db8::1", address);
    }

    [TestMethod]
    public void NormalizeHostName_ShouldStripBracketsAndPort_WhenBracketedIpv6WithPort()
    {
        // Arrange
        var host = "[::1]:443";

        // Act
        var normalized = NormalizeHostName(host);

        // Assert
        Assert.AreEqual("::1", normalized);
    }

    [TestMethod]
    public void NormalizeHostName_ShouldStripPort_WhenHostHasPort()
    {
        // Arrange
        var namedHost = "localhost:5001";
        var ipHost = "127.0.0.1:5001";

        // Act
        var normalizedNamed = NormalizeHostName(namedHost);
        var normalizedIp = NormalizeHostName(ipHost);

        // Assert
        Assert.AreEqual("localhost", normalizedNamed);
        Assert.AreEqual("127.0.0.1", normalizedIp);
    }

    [TestMethod]
    public void TryCheckCertificateAuthority_ShouldFail_WhenCertificateMissing()
    {
        // Arrange
        Sut.TryCleanCertificateAuthority(out _);
        if (Sut.TryCheckCertificateAuthority(out _))
        {
            Assert.Inconclusive("Existing certificate authority detected; cannot validate missing-certificate behavior.");
        }

        // Act
        var result = Sut.TryCheckCertificateAuthority(out var message);

        // Assert
        Assert.IsFalse(result);
        Assert.AreEqual("No valid certificate found.", message);
    }

    [TestMethod]
    public void TryCheckCertificateAuthority_ShouldReturnTrue_WhenCertificateExists()
    {
        // Act
        var result = Sut.TryCheckCertificateAuthority(out var message);

        // Assert
        Assert.IsTrue(result, message);
        Assert.Contains("A valid certificate was found", message);
    }

    [TestMethod]
    public void TryCheckTrustedCertificateAuthority_ShouldFail_WhenNotTrusted()
    {
        // Arrange
        var storeMock = Fixture.Freeze<Mock<ICertificateStore>>();
        storeMock
            .Setup(x => x.TryCheckTrustedCertificate(It.IsAny<X509Certificate2>(), out It.Ref<bool>.IsAny, out It.Ref<string>.IsAny))
            .Returns((X509Certificate2 _, out bool isTrusted, out string error) =>
            {
                isTrusted = false;
                error = "";
                return true;
            });

        // Act
        var result = Sut.TryCheckTrustedCertificateAuthority(out var message);

        // Assert
        Assert.IsFalse(result);
        Assert.Contains("The following certificates were found, but none of them is trusted:", message);
        storeMock.Verify(x => x.TryCheckTrustedCertificate(It.IsAny<X509Certificate2>(), out It.Ref<bool>.IsAny, out It.Ref<string>.IsAny), Times.Once);
    }

    [TestMethod]
    public void TryCheckTrustedCertificateAuthority_ShouldReturnTrue_WhenTrusted()
    {
        // Arrange
        var storeMock = Fixture.Freeze<Mock<ICertificateStore>>();
        storeMock
            .Setup(x => x.TryCheckTrustedCertificate(It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2>(), out It.Ref<bool>.IsAny, out It.Ref<string>.IsAny))
            .Returns((System.Security.Cryptography.X509Certificates.X509Certificate2 _, out bool isTrusted, out string error) =>
            {
                isTrusted = true;
                error = "";
                return true;
            });

        // Act
        var result = Sut.TryCheckTrustedCertificateAuthority(out var message);

        // Assert
        Assert.IsTrue(result, message);
        Assert.AreEqual("A trusted certificate was found", message);
        storeMock.Verify(x => x.TryCheckTrustedCertificate(It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2>(), out It.Ref<bool>.IsAny, out It.Ref<string>.IsAny), Times.Once);
    }

    [TestMethod]
    public void TryCleanCertificateAuthority_ShouldSucceed()
    {
        // Arrange
        var storeMock = Fixture.Freeze<Mock<ICertificateStore>>();
        storeMock
            .Setup(x => x.TryUntrustCertificate(It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2>()))
            .Returns(true);

        // Act
        var result = Sut.TryCleanCertificateAuthority(out var message);

        // Assert
        Assert.IsTrue(result, message);
        Assert.IsFalse(string.IsNullOrWhiteSpace(message), "Expected a success message.");
        storeMock.Verify(x => x.TryUntrustCertificate(It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2>()), Times.AtLeastOnce);
    }

    [TestMethod]
    public void TryCreateCertificateAuthority_ShouldFail_WhenTrustRequestedAndStoreRejects()
    {
        // Arrange
        var storeMock = Fixture.Freeze<Mock<ICertificateStore>>();
        storeMock
            .Setup(x => x.TryTrustCertificate(It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2>(), out It.Ref<string>.IsAny))
            .Returns((System.Security.Cryptography.X509Certificates.X509Certificate2 _, out string error) =>
            {
                error = "trust-failed";
                return false;
            });

        // Act
        var result = Sut.TryCreateCertificateAuthority(trust: true, out var message);

        // Assert
        Assert.IsFalse(result);
        Assert.AreEqual("trust-failed", message);
        storeMock.Verify(x => x.TryTrustCertificate(It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2>(), out It.Ref<string>.IsAny), Times.Once);
    }

    [TestMethod]
    public void TryCreateCertificateAuthority_ShouldSucceed_WhenUntrusted()
    {
        // Act
        var result = Sut.TryCreateCertificateAuthority(trust: false, out var message);

        // Assert
        Assert.IsTrue(result, message);
    }

    [TestMethod]
    public void TryExportCertificateAuthority_ShouldFail_WhenCertificateMissing()
    {
        // Arrange
        var exportPath = Path.GetTempFileName();
        Sut.TryCleanCertificateAuthority(out _);
        if (Sut.TryCheckCertificateAuthority(out _))
        {
            Assert.Inconclusive("Existing certificate authority detected; cannot validate missing-certificate behavior.");
        }

        try
        {
            // Act
            var result = Sut.TryExportCertificateAuthority(exportPath, "password", CertificateExportFormat.Pem, out var message);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual("No valid certificate found.", message);
        }
        finally
        {
            File.Delete(exportPath);
        }
    }

    [TestMethod]
    public void TryExportCertificateAuthority_ShouldFail_WhenExportPathMissing()
    {
        // Act
        var result = Sut.TryExportCertificateAuthority("", "password", CertificateExportFormat.Pem, out var message);

        // Assert
        Assert.IsFalse(result);
        Assert.AreEqual("Export path is required.", message);
    }

    [TestMethod]
    public void TryExportCertificateAuthority_ShouldSucceed_WhenPemAndPassword()
    {
        // Arrange
        var exportPath = Path.GetTempFileName();

        try
        {
            // Act
            var result = Sut.TryExportCertificateAuthority(exportPath, "password", CertificateExportFormat.Pem, out var message);
            var content = File.ReadAllText(exportPath);

            // Assert
            Assert.IsTrue(result, message);
            Assert.Contains("BEGIN ENCRYPTED PRIVATE KEY", content);
        }
        finally
        {
            File.Delete(exportPath);
        }
    }

    [TestMethod]
    public void TryExportCertificateAuthority_ShouldSucceed_WhenPemWithoutPassword()
    {
        // Arrange
        var exportPath = Path.GetTempFileName();

        try
        {
            // Act
            var result = Sut.TryExportCertificateAuthority(exportPath, "", CertificateExportFormat.Pem, out var message);
            var content = File.ReadAllText(exportPath);

            // Assert
            Assert.IsTrue(result, message);
            Assert.Contains("BEGIN PRIVATE KEY", content);
            Assert.IsFalse(content.Contains("BEGIN ENCRYPTED PRIVATE KEY", StringComparison.Ordinal), "Unexpected encrypted private key label.");
        }
        finally
        {
            File.Delete(exportPath);
        }
    }

    [TestMethod]
    public void TryImportCertificateAuthority_ShouldFail_WhenImportPathMissing()
    {
        // Act
        var result = Sut.TryImportCertificateAuthority("", "password", trust: false, out var message);

        // Assert
        Assert.IsFalse(result);
        Assert.AreEqual("Import path is required.", message);
    }

    [TestMethod]
    public void TryImportCertificateAuthority_ShouldFail_WhenImportPathNotFound()
    {
        // Arrange
        var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx");

        // Act
        var result = Sut.TryImportCertificateAuthority(missingPath, "password", trust: false, out var message);

        // Assert
        Assert.IsFalse(result);
        Assert.AreEqual($"Import file not found at '{missingPath}'.", message);
    }

    [TestMethod]
    public void TryImportCertificateAuthority_ShouldSucceed_WhenPfxAndPassword()
    {
        // Arrange
        var storeMock = Fixture.Freeze<Mock<ICertificateStore>>();
        storeMock
            .Setup(x => x.TryUntrustCertificate(It.IsAny<System.Security.Cryptography.X509Certificates.X509Certificate2>()))
            .Returns(true);
        var exportPath = Path.GetTempFileName();

        try
        {
            var exported = Sut.TryExportCertificateAuthority(exportPath, "password", CertificateExportFormat.Pfx, out var exportMessage);
            Assert.IsTrue(exported, exportMessage);

            // Act
            var result = Sut.TryImportCertificateAuthority(exportPath, "password", trust: false, out var message);

            // Assert
            Assert.IsTrue(result, message);
            Assert.AreEqual("Imported CA certificate.", message);
        }
        finally
        {
            Sut.TryCleanCertificateAuthority(out _);
            File.Delete(exportPath);
        }
    }

    private static string NormalizeHostName(string hostName)
    {
        var method = typeof(CertificateManager).GetMethod("NormalizeHostName", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method, "Expected HttpsCertificateManager.NormalizeHostName to exist.");
        var sut = new CertificateManager(new Mock<ICertificateStore>().Object, Options.Create(new CertificateOptions()));
        return (string)method.Invoke(sut, [hostName])!;
    }
}
