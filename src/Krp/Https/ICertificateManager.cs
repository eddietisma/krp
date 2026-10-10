using System.Security.Cryptography.X509Certificates;

namespace Krp.Https;

public interface ICertificateManager
{
    X509Certificate2 GetOrCreateServerCertificate(string hostName);

    bool TryCheckCertificateAuthority(out string message);
    bool TryCheckTrustedCertificateAuthority(out string message);
    bool TryCleanCertificateAuthority(out string message);
    bool TryCreateCertificateAuthority(bool trust, out string message);
    bool TryExportCertificateAuthority(string exportPath, string exportPassword, CertificateExportFormat exportFormat, out string message);
    bool TryImportCertificateAuthority(string importPath, string importPassword, bool trust, out string message);
}

