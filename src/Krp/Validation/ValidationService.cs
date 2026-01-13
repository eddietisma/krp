using k8s;
using Krp.Common;
using Krp.Dns;
using Krp.Endpoints;
using Krp.Kubernetes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Validation;

public class ValidationService : IHostedService
{
    private readonly ILogger<ValidationService> _logger;
    private readonly IOptions<DnsHostsOptions> _dnsOptions;
    private readonly EndpointManager _endpointManager;
    private readonly KubernetesClient _kubernetesClient;
    private readonly IDnsHandler _dnsHandler;

    public ValidationService(EndpointManager endpointManager, KubernetesClient kubernetesClient, IDnsHandler dnsHandler, ILogger<ValidationService> logger, IOptions<DnsHostsOptions> dnsOptions)
    {
        _endpointManager = endpointManager;
        _kubernetesClient = kubernetesClient;
        _dnsHandler = dnsHandler;
        _logger = logger;
        _dnsOptions = dnsOptions;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var hostsPath = _dnsOptions.Value.Path;

        _logger.LogInformation($"✅ Platform: {RuntimeInformation.OSArchitecture}");

        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            _logger.LogInformation("✅ Detected running inside docker container");
            _logger.LogInformation("    - All traffic will be forwarded to host.docker.internal");
            _logger.LogInformation("    - Endpoint targets will be selected using IP:PORT");
        }

        var validationSuccess = ValidateRouting(hostsPath);
        if (!validationSuccess)
        {
            _logger.LogError("Validation failed. Terminating...");
            Environment.Exit(1);
        }

        validationSuccess = await ValidateKubernetes() && validationSuccess;
        if (!validationSuccess)
        {
            _logger.LogError("Validation failed. Terminating...");
            Environment.Exit(1);
        }

        _endpointManager.Initialize();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
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
            }
            else if (ValidateIsWinDivertInstalled())
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

    private async Task<bool> ValidateKubernetes()
    {
        var fileExists = File.Exists(KubernetesClientConfiguration.KubeConfigDefaultLocation);
        if (fileExists)
        {
            _logger.LogInformation("✅ Found kubeconfig: '{ConfigPath}'", KubernetesClientConfiguration.KubeConfigDefaultLocation);
        }
        else
        {
            _logger.LogError("❌ Kubeconfig not found: '{ConfigPath}'", KubernetesClientConfiguration.KubeConfigDefaultLocation);
            return false;
        }

        var timeoutSeconds = 60;
        var hasAccess = _kubernetesClient.WaitForAccess(TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromSeconds(2));
        if (hasAccess)
        {
            var context = await _kubernetesClient.FetchCurrentContext();
            _logger.LogInformation("✅ Successfully connected to {Context}", context);
        }
        else
        {
            _logger.LogError("❌ Unable to reach Kubernetes ({timeout}s timeout).", timeoutSeconds);
            _logger.LogError("    - Authenticate or refresh credentials");
            _logger.LogError("    - Set KUBECONFIG to a valid file and retry");
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
}