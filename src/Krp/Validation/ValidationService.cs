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
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Validation;

public class ValidationService : IHostedService
{
    private readonly ILogger<ValidationService> _logger;
    private readonly IOptions<DnsHostsOptions> _dnsOptions;
    private readonly EndpointManager _endpointManager;
    private readonly KubernetesClient _kubernetesClient;

    public ValidationService(EndpointManager endpointManager, KubernetesClient kubernetesClient, ILogger<ValidationService> logger, IOptions<DnsHostsOptions> dnsOptions)
    {
        _endpointManager = endpointManager;
        _kubernetesClient = kubernetesClient;
        _logger = logger;
        _dnsOptions = dnsOptions;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var hostsPath = _dnsOptions.Value.Path;
        
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            _logger.LogInformation("✅ Detected running inside docker container");
            _logger.LogInformation("    - All traffic will be forwarded to host.docker.internal");
            _logger.LogInformation("    - Endpoint targets will be selected using IP:PORT");
        }

        var validationSuccess = string.IsNullOrEmpty(hostsPath) || ValidateHosts(hostsPath);
        validationSuccess = await ValidateKubernetes() && validationSuccess;
        
        if (!validationSuccess)
        {
            _logger.LogError("Validation failed. Terminating...");
            Environment.Exit(1);
        }

        _endpointManager.Initialize();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    
    private  bool ValidateHosts(string hostsPath)
    {
        var fileExists = File.Exists(hostsPath);
        var hasAccess = FileHelper.HasWriteAccess(hostsPath);

        _logger.LogInformation(fileExists ? "✅ Found hosts file: '{HostsPath}'" : "❌ Hosts file not found: '{HostsPath}'", hostsPath);
        _logger.LogInformation(hasAccess ? "✅ Permission to hosts file" : "❌ Write-access to hosts file is denied");

        return fileExists && hasAccess;
    }

    private async Task<bool> ValidateKubernetes()
    {
        var fileExists = File.Exists(KubernetesClientConfiguration.KubeConfigDefaultLocation);

        _logger.LogInformation(fileExists ? "✅ Found kubeconfig: '{ConfigPath}'" : "❌ Kubeconfig not found: '{ConfigPath}'", KubernetesClientConfiguration.KubeConfigDefaultLocation);

        var hasAccess = _kubernetesClient.WaitForAccess(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
        if (hasAccess)
        {
            var context = await _kubernetesClient.FetchCurrentContext();
            _logger.LogInformation("✅ Successfully connected to {Context}", context);
        }
        else
        {
            _logger.LogInformation(
                "\u001b[31m❌ Unable to reach Kubernetes (30s timeout).\n" +
                "   • Kube-config: {ConfigPath}\n" +
                "   • Authenticate or refresh credentials using your cloud CLI:\n" +
                "     ▸ Azure :  az login …\n" +
                "     ▸ AWS   :  aws login …\n" +
                "     ▸ GCP   :  gcloud auth login …\n" +
                "   • Or set KUBECONFIG to a valid file and retry.\u001b[0m",
                KubernetesClientConfiguration.KubeConfigDefaultLocation);
        }

        return fileExists && hasAccess;
    }
}