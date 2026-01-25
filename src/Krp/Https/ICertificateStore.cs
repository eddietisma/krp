using System.Security.Cryptography.X509Certificates;

namespace Krp.Https;

public interface ICertificateStore
{
    bool TryCheckTrustedCertificate(X509Certificate2 certificate, out bool isTrusted, out string error);
    bool TryTrustCertificate(X509Certificate2 certificate, out string error);
    bool TryUntrustCertificate(X509Certificate2 certificate);
}
