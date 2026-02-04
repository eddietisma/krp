using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace Krp.Https;

public class CertificateManager : ICertificateManager
{
    private static readonly Lock _lock = new();
    private static readonly Dictionary<string, X509Certificate2> _leafCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICertificateStore _certificateStore;
    private readonly CertificateOptions _options;

    public CertificateManager(ICertificateStore certificateStore, IOptions<CertificateOptions> options)
    {
        _certificateStore = certificateStore;
        _options = options.Value;
    }
    
    public X509Certificate2 GetOrCreateServerCertificate(string hostName)
    {
        var normalizedHost = NormalizeHostName(hostName);

        lock (_lock)
        {
            if (_leafCache.TryGetValue(normalizedHost, out var cached))
            {
                return cached;
            }

            using var caCertificate = FindCertificateAuthority(requirePrivateKey: true) ?? throw new InvalidOperationException("No valid certificate found.");
            using var rsa = RSA.Create(2048);

            var request = new CertificateRequest($"CN={normalizedHost}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var sanBuilder = new SubjectAlternativeNameBuilder();

            if (IPAddress.TryParse(normalizedHost, out var ipAddress))
            {
                sanBuilder.AddIpAddress(ipAddress);
            }
            else
            {
                sanBuilder.AddDnsName(normalizedHost);
            }

            request.CertificateExtensions.Add(sanBuilder.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            var serial = new byte[16];
            RandomNumberGenerator.Fill(serial);

            using var issuedCert = request.Create(caCertificate, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2), serial);

            var certWithKey = issuedCert.CopyWithPrivateKey(rsa);
            _leafCache[normalizedHost] = certWithKey;
            return certWithKey;
        }
    }

    public bool TryCheckCertificateAuthority(out string message)
    {
        var certificate = FindCertificateAuthority(requirePrivateKey: true);
        if (certificate == null)
        {
            message = "No valid certificate found.";
            return false;
        }

        var notBefore = certificate.NotBefore.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'");
        var notAfter = certificate.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'");
        message = $"A valid certificate was found: {certificate.Thumbprint} - {certificate.Subject} - Valid from {notBefore} to {notAfter}";
        return true;
    }

    public bool TryCheckTrustedCertificateAuthority(out string message)
    {
        var certificate = FindCertificateAuthority(requirePrivateKey: true);
        if (certificate == null)
        {
            message = "No valid certificate found.";
            return false;
        }

        var notBefore = certificate.NotBefore.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'");
        var notAfter = certificate.NotAfter.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'");
        var summary = $"{certificate.Thumbprint} - {certificate.Subject} - Valid from {notBefore} to {notAfter}";

        if (!_certificateStore.TryCheckTrustedCertificate(certificate, out var isTrusted, out var trustError))
        {
            message = trustError;
            return false;
        }

        if (!isTrusted)
        {
            message =  $"The following certificates were found, but none of them is trusted: 1 certificate{Environment.NewLine}    1) {summary}";
            return false;
        }

        message = "A trusted certificate was found";
        return true;
    }

    public bool TryCleanCertificateAuthority(out string message)
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);

            var matches = store.Certificates
                .Find(X509FindType.FindBySubjectDistinguishedName, _options.DefaultCertificateAuthoritySubject, false)
                .Where(x => x.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault()?.CertificateAuthority == true)
                .ToList();

            if (matches.Count == 0)
            {
                message = "HTTPS certificates successfully removed from the machine.";
                return true;
            }

            foreach (var certificate in matches)
            {
                _certificateStore.TryUntrustCertificate(certificate);
                store.Remove(certificate);
            }

            message = "HTTPS certificates successfully removed from the machine.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to remove CA certificate: {ex.Message}";
            return false;
        }
    }

    public bool TryCreateCertificateAuthority(bool trust, out string message)
    {
        var existing = FindCertificateAuthority(requirePrivateKey: true);
        if (existing != null)
        {
            if (trust && !_certificateStore.TryTrustCertificate(existing, out var trustError))
            {
                message = trustError;
                return false;
            }

            message = "A valid HTTPS certificate is already present.";
            return true;
        }

        try
        {
            using var certificate = CreateCertificateAuthority();
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);

            if (trust && !_certificateStore.TryTrustCertificate(certificate, out var trustError))
            {
                message = trustError;
                return false;
            }

            message = trust
                ? "Successfully created and trusted a new HTTPS certificate."
                : "The HTTPS developer certificate was generated successfully.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to create CA certificate: {ex.Message}";
            return false;
        }
    }

    public bool TryExportCertificateAuthority(string exportPath, string exportPassword, CertificateExportFormat exportFormat, out string message)
    {
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            message = "Export path is required.";
            return false;
        }
        
        var certificate = FindCertificateAuthority(requirePrivateKey: true);
        if (certificate == null)
        {
            message = "No valid certificate found.";
            return false;
        }

        if (exportFormat == CertificateExportFormat.Pem)
        {
            return TryExportPem(certificate, exportPassword, exportPath, out message);
        }

        try
        {
            var export = certificate.Export(X509ContentType.Pfx, exportPassword);
            File.WriteAllBytes(exportPath, export);
            message = $"Exported HTTPS certificate to '{exportPath}'.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to export HTTPS certificate: {ex.Message}";
            return false;
        }
    }

    public bool TryImportCertificateAuthority(string importPath, string importPassword, bool trust, out string message)
    {
        if (string.IsNullOrWhiteSpace(importPath))
        {
            message = "Import path is required.";
            return false;
        }

        if (!File.Exists(importPath))
        {
            message = $"Import file not found at '{importPath}'.";
            return false;
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(importPath, importPassword, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificate);

            if (trust && !_certificateStore.TryTrustCertificate(certificate, out var trustError))
            {
                message = trustError;
                return false;
            }

            message = "Imported CA certificate.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to import CA certificate: {ex.Message}";
            return false;
        }
    }

    private static string BuildPemBlock(string label, byte[] bytes)
    {
        var base64 = Convert.ToBase64String(bytes);
        var lines = Enumerable.Range(0, (base64.Length + 63) / 64)
            .Select(x => base64.Substring(x * 64, Math.Min(64, base64.Length - (x * 64))));
        return $"-----BEGIN {label}-----{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{Environment.NewLine}-----END {label}-----{Environment.NewLine}";
    }

    private X509Certificate2 CreateCertificateAuthority()
    {
        using var rsa = RSA.Create(4096);
        var request = new CertificateRequest(_options.DefaultCertificateAuthoritySubject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        var export = certificate.Export(X509ContentType.Pfx, string.Empty);
        var ca = X509CertificateLoader.LoadPkcs12(export, string.Empty, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
        return ca;
    }

    private X509Certificate2? FindCertificateAuthority(bool requirePrivateKey)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var matches = store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, _options.DefaultCertificateAuthoritySubject, false);
        var candidates = matches
            .Where(x => x.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault()?.CertificateAuthority == true)
            .Where(x => x.NotBefore.ToUniversalTime() <= DateTime.UtcNow && x.NotAfter.ToUniversalTime() >= DateTime.UtcNow)
            .Where(x =>
            { 
                if (!requirePrivateKey)
                {
                    return true;
                }

                try
                {
                    using var rsa = x.GetRSAPrivateKey();
                    return rsa != null;
                }
                catch
                {
                    return false;
                }
            })
            .OrderByDescending(x => x.NotAfter)
            .ToList();

        return candidates.FirstOrDefault();
    }

    private string NormalizeHostName(string hostName)
    {
        var value = string.IsNullOrWhiteSpace(hostName) ? _options.DefaultHostName : hostName.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return _options.DefaultHostName;
        }

        // Handle bracketed IPv6, with optional port: [::1] or [::1]:443
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var closingBracket = value.IndexOf(']');
            if (closingBracket > 0)
            {
                var insideBrackets = value.Substring(1, closingBracket - 1);
                return string.IsNullOrWhiteSpace(insideBrackets) ? _options.DefaultHostName : insideBrackets;
            }
        }

        // Preserve bare IPv6 literals like "::1" (and IPv4 literals) as-is.
        if (IPAddress.TryParse(value, out _))
        {
            return value;
        }

        var colon = value.LastIndexOf(':');
        if (colon > 0)
        {
            var portPart = value[(colon + 1)..];
            if (string.IsNullOrEmpty(portPart) || portPart.All(char.IsDigit))
            {
                value = value[..colon];
            }
        }

        return string.IsNullOrWhiteSpace(value) ? _options.DefaultHostName : value;
    }

    private static bool TryExportPem(X509Certificate2 certificate, string exportPassword, string exportPath, out string message)
    {
        try
        {
            var certPem = BuildPemBlock("CERTIFICATE", certificate.Export(X509ContentType.Cert));
            var privateKeyPem = "";

            var privateKeyLabel = string.IsNullOrEmpty(exportPassword)
                ? "PRIVATE KEY"
                : "ENCRYPTED PRIVATE KEY";

            using var rsa = certificate.GetRSAPrivateKey();
            if (rsa != null)
            {
                byte[] keyBytes;
                if (string.IsNullOrEmpty(exportPassword))
                {
                    keyBytes = rsa.ExportPkcs8PrivateKey();
                }
                else
                {
                    var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000);
                    keyBytes = rsa.ExportEncryptedPkcs8PrivateKey(exportPassword, pbe);
                }

                privateKeyPem = BuildPemBlock(privateKeyLabel, keyBytes);
            }

            using var ecdsa = certificate.GetECDsaPrivateKey();
            if (ecdsa != null)
            {
                byte[] keyBytes;
                if (string.IsNullOrEmpty(exportPassword))
                {
                    keyBytes = ecdsa.ExportPkcs8PrivateKey();
                }
                else
                {
                    var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000);
                    keyBytes = ecdsa.ExportEncryptedPkcs8PrivateKey(exportPassword, pbe);
                }

                privateKeyPem = BuildPemBlock(privateKeyLabel, keyBytes);
            }

            File.WriteAllText(exportPath, certPem + privateKeyPem);
            message = $"Exported HTTPS certificate to '{exportPath}'.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to export HTTPS certificate: {ex.Message}";
            return false;
        }
    }
}
