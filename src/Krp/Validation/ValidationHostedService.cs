using Krp.Common;
using Krp.Dns;
using Krp.Endpoints;
using Krp.Https;
using Krp.Kubernetes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Validation;

public class ValidationHostedService : BackgroundService
{
    private readonly ILogger<ValidationHostedService> _logger;
    private readonly DnsHostsOptions _dnsOptions;
    private readonly IEndpointManager _endpointManager;
    private readonly IDnsHandler _dnsHandler;
    private readonly ICertificateManager _certificateManager;
    private readonly IKubernetesClient _kubernetesClient;
    private readonly ValidationState _validationState;
    private readonly ValidationOptions _validationOptions;
    private readonly IHostApplicationLifetime _appLifetime;

    public ValidationHostedService(
        IEndpointManager endpointManager,
        ICertificateManager certificateManager,
        IDnsHandler dnsHandler,
        IKubernetesClient kubernetesClient,
        ValidationState validationState,
        IHostApplicationLifetime appLifetime,
        ILogger<ValidationHostedService> logger,
        IOptions<DnsHostsOptions> dnsOptions,
        IOptions<ValidationOptions> validationOptions)
    {
        _endpointManager = endpointManager;
        _certificateManager = certificateManager;
        _dnsHandler = dnsHandler;
        _kubernetesClient = kubernetesClient;
        _validationState = validationState;
        _appLifetime = appLifetime;
        _logger = logger;
        _dnsOptions = dnsOptions.Value;
        _validationOptions = validationOptions.Value;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var os = RuntimeInformation.OSDescription.Trim();
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        _logger.LogInformation($"✅ Platform: {os}/{arch}");

        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            _logger.LogInformation("✅ Detected running inside docker container");
            _logger.LogInformation("    - All traffic will be forwarded to host.docker.internal");
            _logger.LogInformation("    - Endpoint targets will be selected using IP:PORT");
            _logger.LogInformation("    - Loopback client restriction is off");
        }

        var validRouting = ValidateRouting(_dnsOptions.Path);
        var validKubernetesConfig = ValidateKubernetesConfig();
        var validHttpsCertificate = ValidateHttpsCertificateAuthority();

        if (!validRouting || !validKubernetesConfig || !validHttpsCertificate)
        {
            _validationState.MarkCompleted(false);

            if (!_validationOptions.ExitOnFailure)
            {
                return Task.CompletedTask;
            }

            _logger.LogError("Terminating...");
            Environment.ExitCode = 1;
            _appLifetime.StopApplication();

            return Task.CompletedTask;
        }

        _endpointManager.Initialize();
        _validationState.MarkCompleted(true);
        return Task.CompletedTask;
    }

    private bool ValidateRouting(string hostsPath)
    {
        var routing = _dnsHandler.GetType();

        var routingName = routing.Name switch
        {
            nameof(DnsHostsHandler) => "hosts",
            nameof(DnsWinDivertHandler) => "windivert",
            _ => "unknown",
        };

        _logger.LogInformation($"✅ Using routing: {routingName}");

        if (routing == typeof(DnsWinDivertHandler))
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger.LogError("❌ WinDivert routing is only supported on Windows platforms");
                return false;
            }

            if (!ValidateWinDivertDllPresent())
            {
                _logger.LogError("❌ WinDivert DLL not found");
                return false;
            }

            if (ValidateIsWinDivertInstalled())
            {
                _logger.LogInformation("✅ Found windows service: WinDivert");
            }
            else if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                _logger.LogError("❌ WinDivert routing requires administrator for installing");
                return false;
            }

            return true;
        }

        if (routing == typeof(DnsHostsHandler))
        {
            var fileExists = File.Exists(hostsPath);
            if (fileExists)
            {
                _logger.LogInformation("✅ Found hosts file: '{HostsPath}'", hostsPath);
            }
            else
            {
                _logger.LogError("❌ Hosts file not found: '{HostsPath}'", hostsPath);
                return false;
            }

            var hasAccess = FileHelper.HasWriteAccess(hostsPath);
            if (hasAccess)
            {
                _logger.LogInformation("✅ Permission to hosts file");
            }
            else
            {
                _logger.LogError("❌ Write-access to hosts file is denied");
                return false;
            }
        }

        return true;
    }

    private bool ValidateKubernetesConfig()
    {
        if (_kubernetesClient.TryGetKubeConfigPath(out var configPath))
        {
            _logger.LogInformation("✅ Found kubeconfig: '{ConfigPath}'", configPath);
        }
        else
        {
            _logger.LogError("❌ Kubeconfig not found: '{ConfigPath}'", configPath);
            return false;
        }

        return true;
    }
    
    private static bool ValidateIsWinDivertInstalled()
    {
#pragma warning disable CA1416
        try
        {
            return new ServiceController("windivert").Status is
                ServiceControllerStatus.Running or
                ServiceControllerStatus.Stopped or
                ServiceControllerStatus.Paused or
                ServiceControllerStatus.StartPending or
                ServiceControllerStatus.StopPending;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
#pragma warning restore CA1416
    }

    private static bool ValidateWinDivertDllPresent()
    {
        var baseDir = ExecutablePathHelper.GetExecutableBaseDirectory();
        var dllPaths = new[]
        {
            Path.Combine(baseDir, "runtimes", "win-x86", "native", "WinDivert.dll"),
            Path.Combine(baseDir, "runtimes", "win-x64", "native", "WinDivert.dll"),
        };

        return dllPaths.Any(File.Exists);
    }

    private bool ValidateHttpsCertificateAuthority()
    {
        const bool result = true; // HTTPS errors are just informational since its optional.

        if (_certificateManager.TryCheckTrustedCertificateAuthority(out _))
        {
            _logger.LogInformation("✅ HTTPS certificate: OK");
            return result;
        }

        if (_certificateManager.TryCheckCertificateAuthority(out _))
        {
            _logger.LogWarning("⚠️ HTTPS certificate: Untrusted - run `krp https --trust` to enable HTTPS");
            return result;
        }

        _logger.LogWarning("⚠️ HTTPS certificate: Disabled - run `krp https --trust` to enable HTTPS");
        return result;
    }
}
