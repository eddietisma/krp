using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Krp.Https.CertificateStores;

public class LinuxCertificateStore : ICertificateStore
{
    public bool TryCheckTrustedCertificate(X509Certificate2 certificate, out bool isTrusted, out string error)
    {
        if (!TryGetTrustSettings(out var trustPath, out _, out _))
        {
            isTrusted = false;
            error = "Linux trust store tooling was not detected. Install update-ca-certificates or update-ca-trust.";
            return false;
        }

        if (!File.Exists(trustPath))
        {
            isTrusted = false;
            error = string.Empty;
            return true;
        }

        try
        {
            using var installed = X509Certificate2.CreateFromPemFile(trustPath);
            isTrusted = string.Equals(installed.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            isTrusted = false;
            error = $"Failed to read installed certificate: {ex.Message}";
            return false;
        }
    }

    public bool TryTrustCertificate(X509Certificate2 certificate, out string error)
    {
        if (!TryGetTrustSettings(out var trustPath, out var commandPath, out var commandArgs))
        {
            error = "Linux trust store tooling was not detected. Install update-ca-certificates or update-ca-trust.";
            return false;
        }

        try
        {
            var trustDir = Path.GetDirectoryName(trustPath);
            if (string.IsNullOrWhiteSpace(trustDir))
            {
                error = "Invalid trust path for Linux certificate store.";
                return false;
            }
            Directory.CreateDirectory(trustDir);
            File.WriteAllText(trustPath, BuildPem(certificate));
        }
        catch (Exception ex)
        {
            error = $"Failed to write trust certificate: {ex.Message}";
            return false;
        }

        if (!TryRunCommand(commandPath, commandArgs, out _, out var commandError))
        {
            error = string.IsNullOrWhiteSpace(commandError)
                ? "There was an error trusting the HTTPS developer certificate."
                : commandError.Trim();
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryUntrustCertificate(X509Certificate2 certificate)
    {
        if (!TryGetTrustSettings(out var trustPath, out var commandPath, out var commandArgs))
        {
            return false;
        }

        try
        {
            if (File.Exists(trustPath))
            {
                File.Delete(trustPath);
            }
        }
        catch
        {
            return false;
        }

        return TryRunCommand(commandPath, commandArgs, out _, out _);
    }

    private static string BuildPem(X509Certificate2 certificate)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN CERTIFICATE-----");
        var base64 = Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
        for (var i = 0; i < base64.Length; i += 64)
        {
            builder.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
        }
        builder.AppendLine("-----END CERTIFICATE-----");
        return builder.ToString();
    }

    private static bool TryGetTrustSettings(out string trustPath, out string commandPath, out string[]? commandArgs)
    {
        var updateCaCertificates = File.Exists("/usr/sbin/update-ca-certificates")
            ? "/usr/sbin/update-ca-certificates"
            : File.Exists("/usr/bin/update-ca-certificates") ? "/usr/bin/update-ca-certificates" : null;

        if (updateCaCertificates != null)
        {
            trustPath = "/usr/local/share/ca-certificates/krp.crt";
            commandPath = updateCaCertificates;
            commandArgs = [];
            return true;
        }

        var updateCaTrust = File.Exists("/usr/sbin/update-ca-trust")
            ? "/usr/sbin/update-ca-trust"
            : File.Exists("/usr/bin/update-ca-trust") ? "/usr/bin/update-ca-trust" : null;

        if (updateCaTrust != null)
        {
            trustPath = "/etc/pki/ca-trust/source/anchors/krp.crt";
            commandPath = updateCaTrust;
            commandArgs = ["extract"];
            return true;
        }

        trustPath = string.Empty;
        commandPath = string.Empty;
        commandArgs = null;
        return false;
    }

    private static bool TryRunCommand(string fileName, string[]? args, out string output, out string error)
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
