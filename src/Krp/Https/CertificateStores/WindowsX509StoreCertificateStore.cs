using System;
using System.Security.Cryptography.X509Certificates;

namespace Krp.Https.CertificateStores;

public class WindowsX509StoreCertificateStore : ICertificateStore
{
    public bool TryCheckTrustedCertificate(X509Certificate2 certificate, out bool isTrusted, out string error)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var existing = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false);
            isTrusted = existing.Count > 0;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            isTrusted = false;
            error = $"Failed to check trust store: {ex.Message}";
            return false;
        }
    }

    public bool TryTrustCertificate(X509Certificate2 certificate, out string error)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            var existing = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false);
            if (existing.Count == 0)
            {
                store.Add(certificate);
            }

            error = string.Empty;
            return true;
        }
        catch
        {
            error = "There was an error trusting the HTTPS developer certificate.";
            return false;
        }
    }

    public bool TryUntrustCertificate(X509Certificate2 certificate)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            var existing = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false);
            if (existing.Count > 0)
            {
                store.Remove(existing[0]);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
