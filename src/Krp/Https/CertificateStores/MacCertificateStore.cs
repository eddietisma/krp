using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace Krp.Https.CertificateStores;

public class MacCertificateStore : ICertificateStore
{
    public bool TryCheckTrustedCertificate(X509Certificate2 certificate, out bool isTrusted, out string error)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"krp-verify-{Guid.NewGuid():N}.cer");
        try
        {
            File.WriteAllBytes(tempPath, certificate.Export(X509ContentType.Cert));

            var ok = TryRunCommand("/usr/bin/security", new[] { "verify-cert", "-c", tempPath }, out var stdout, out var stderr);

            isTrusted = ok;
            error = ok ? string.Empty : (string.IsNullOrWhiteSpace(stderr) ? (stdout?.Trim() ?? string.Empty) : stderr.Trim());
            return true;
        }
        catch (Exception ex)
        {
            isTrusted = false;
            error = ex.Message;
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    public bool TryTrustCertificate(X509Certificate2 certificate, out string error)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"krp-cert-{Guid.NewGuid():N}.cer");
        try
        {
            File.WriteAllBytes(tempPath, certificate.Export(X509ContentType.Cert));

            var loginKeychain = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library/Keychains/login.keychain-db");

            var args = new[] { "add-trusted-cert", "-d", "-r", "trustRoot", "-k", loginKeychain, tempPath };

            if (!TryRunCommand("/usr/bin/security", args, out var stdout, out var stderr))
            {
                error = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim()
                    : !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim()
                    : "There was an error trusting the certificate (macOS Keychain).";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = $"Failed to trust certificate: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort.
            }
        }

        error = string.Empty;
        return true;
    }

    public bool TryUntrustCertificate(X509Certificate2 certificate)
    {
        var thumbprint = certificate.Thumbprint;
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return false;
        }

        var loginKeychain = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library/Keychains/login.keychain-db");
        var systemKeychain = "/Library/Keychains/System.keychain";

        var loginArgs = new[] { "delete-certificate", "-Z", thumbprint, loginKeychain };
        var systemArgs = new[] { "delete-certificate", "-Z", thumbprint, systemKeychain };

        var removedFromLogin = TryRunCommand("/usr/bin/security", loginArgs, out _, out _);
        var removedFromSystem = TryRunCommand("/usr/bin/security", systemArgs, out _, out _);
        return removedFromLogin || removedFromSystem;
    }

    private static bool TryRunCommand(string fileName, string[] args, out string output, out string error)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var arg in args ?? [])
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                output = "";
                error = "Failed to start process.";
                return false;
            }

            output = process.StandardOutput.ReadToEnd();
            error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            output = "";
            error = ex.Message;
            return false;
        }
    }
}
