using Krp.Https;
using McMaster.Extensions.CommandLineUtils;
using System;
using System.Threading.Tasks;

namespace Krp.Tool.Commands;

[Command("https", Description = "Manage HTTPS certificates")]
public sealed class HttpsCommand
{
    private readonly ICertificateManager _httpsCertificateManager;

    [Option("--clean", Description = "Remove the HTTPS certificate from CurrentUser stores")]
    public bool Clean { get; set; }

    [Option("--check", Description = "Check for a valid certificate; with --trust also verify trust")]
    public bool Check { get; set; }

    [Option("--export <PATH>", Description = "Export the HTTPS certificate to a file (PFX export requires --password)")]
    public string ExportPath { get; set; }

    [Option("--format <FORMAT>", Description = "Export format: pfx or pem (default: pfx)")]
    public CertificateExportFormat ExportFormat { get; set; } = CertificateExportFormat.Pfx;

    [Option("--import <PATH>", Description = "Import a certificate PFX into CurrentUser store")]
    public string ImportPath { get; set; }

    [Option("-p|--password <PASSWORD>", Description = "Password for import; required for PFX export")]
    public string Password { get; set; } = "";

    [Option("--trust", Description = "Trust the certificate on this machine, creating one if missing")]
    public bool Trust { get; set; }
    
    public HttpsCommand(ICertificateManager httpsCertificateManager)
    {
        _httpsCertificateManager = httpsCertificateManager;
    }

    public Task<int> OnExecuteAsync(CommandLineApplication _)
    {
        if (Clean && Trust)
        {
            Console.Error.WriteLine("Incompatible options: --clean cannot be combined with --trust.");
            return Task.FromResult(1);
        }

        if (Clean && Check)
        {
            Console.Error.WriteLine("Incompatible options: --clean cannot be combined with --check.");
            return Task.FromResult(1);
        }

        if (Clean && (!string.IsNullOrWhiteSpace(ImportPath) || !string.IsNullOrWhiteSpace(ExportPath)))
        {
            Console.Error.WriteLine("Incompatible options: --clean cannot be combined with --import or --export.");
            return Task.FromResult(1);
        }

        if (Check && (!string.IsNullOrWhiteSpace(ImportPath) || !string.IsNullOrWhiteSpace(ExportPath)))
        {
            Console.Error.WriteLine("Incompatible options: --check cannot be combined with --import or --export.");
            return Task.FromResult(1);
        }

        if (!string.IsNullOrWhiteSpace(ImportPath) && !string.IsNullOrWhiteSpace(ExportPath))
        {
            Console.Error.WriteLine("Incompatible options: --import cannot be combined with --export.");
            return Task.FromResult(1);
        }

        if (Trust && !string.IsNullOrWhiteSpace(ExportPath))
        {
            Console.Error.WriteLine("Incompatible options: --trust cannot be combined with --export.");
            return Task.FromResult(1);
        }

        if (Check && Trust)
        {
            if (!_httpsCertificateManager.TryCheckTrustedCertificateAuthority(out var trustCheckMessage))
            {
                Console.Error.WriteLine(trustCheckMessage);
                return Task.FromResult(1);
            }

            Console.WriteLine(trustCheckMessage);
            return Task.FromResult(0);
        }

        if (Clean)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Cleaning HTTPS development certificates from the machine. A prompt might get displayed to confirm the removal of some of the certificates.");
            Console.ResetColor();

            if (!_httpsCertificateManager.TryCleanCertificateAuthority(out var cleanMessage))
            {
                Console.Error.WriteLine(cleanMessage);
                return Task.FromResult(1);
            }

            Console.WriteLine(cleanMessage);
            return Task.FromResult(0);
        }

        if (Check)
        {
            if (!_httpsCertificateManager.TryCheckCertificateAuthority(out var checkMessage))
            {
                Console.Error.WriteLine(checkMessage);
                return Task.FromResult(1);
            }

            Console.WriteLine(checkMessage);
            Console.WriteLine("Run the command with both --check and --trust options to ensure that the certificate is not only valid but also trusted.");
            return Task.FromResult(0);
        }

        if (!string.IsNullOrWhiteSpace(ImportPath))
        {
            if (Trust)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Trusting the HTTPS development certificate was requested. A confirmation prompt will be displayed if the certificate was not previously trusted. Click yes on the prompt to trust the certificate.");
                Console.ResetColor();
            }

            if (!_httpsCertificateManager.TryImportCertificateAuthority(ImportPath, Password, Trust, out var importMessage))
            {
                Console.Error.WriteLine(importMessage);
                return Task.FromResult(1);
            }

            Console.WriteLine(importMessage);
            return Task.FromResult(0);
        }

        if (!string.IsNullOrWhiteSpace(ExportPath))
        {
            if (ExportFormat == CertificateExportFormat.Pfx && string.IsNullOrWhiteSpace(Password))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("PFX export requires a password. Use -p|--password <PASSWORD>.");
                Console.ResetColor();
                return Task.FromResult(1);
            }

            if (!_httpsCertificateManager.TryExportCertificateAuthority(ExportPath, Password, ExportFormat, out var exportMessage))
            {
                Console.Error.WriteLine(exportMessage);
                return Task.FromResult(1);
            }

            Console.WriteLine(exportMessage);
            return Task.FromResult(0);
        }

        if (Trust)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Trusting the HTTPS development certificate was requested. A confirmation prompt will be displayed if the certificate was not previously trusted. Click yes on the prompt to trust the certificate.");
            Console.ResetColor();

            if (!_httpsCertificateManager.TryCreateCertificateAuthority(trust: true, out var trustMessage))
            {
                Console.Error.WriteLine(trustMessage);
                return Task.FromResult(1);
            }

            Console.WriteLine(trustMessage);
            return Task.FromResult(0);
        }

        // Default action: create a certificate authority (untrusted) if no other option was specified.
        if (!_httpsCertificateManager.TryCreateCertificateAuthority(trust: false, out var message))
        {
            Console.Error.WriteLine(message);
            return Task.FromResult(1);
        }

        Console.WriteLine(message);
        return Task.FromResult(0);
    }

}
