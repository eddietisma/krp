using Krp.Common;
using Krp.DependencyInjection;
using Krp.Dns;
using Krp.Forwarders.HttpForwarder;
using Krp.Logging;
using Krp.Tool.TerminalUi;
using Krp.Tool.TerminalUi.DependencyInjection;
using Krp.Tool.TerminalUi.Logging;
using Krp.Validation;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Tool.Commands;

[Command(Name = "krp", OptionsComparison = StringComparison.InvariantCultureIgnoreCase)]
[Subcommand(typeof(HttpsCommand))]
[VersionOptionFromMember("--version|-v", MemberName = nameof(GetVersion))]
public class RootCommand
{
    [Option("--nameserver|-n <NAMESERVERS>", Description = "DNS server, used for HTTP proxy endpoints")]
    public string Nameserver { get; set; } = "8.8.8.8";

    [Option("--no-certificate-validation", Description = "Disable certificate validation")]
    public bool NoCertificateValidation { get; set; } = true;

    [Option("--no-discovery", Description = "Disable automatic Kubernetes endpoint discovery")]
    public bool NoDiscovery { get; set; } = false;

    [Option("--no-ui", Description = "Disable terminal UI")]
    public bool NoTerminalUi { get; set; } = false;
    
    [Option("--forwarder|-f <FORWARDER>", Description = "Forwarding method")]
    [AllowedValues("tcp", "http", "hybrid", IgnoreCase = true)]
    public string Forwarder { get; set; } = "hybrid";

    [Option("--routing|-r <ROUTING>", Description = "Routing method")]
    [AllowedValues("hosts", "windivert", IgnoreCase = true)]
    public string Routing { get; set; }

    public RootCommand()
    {
        Routing = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && (RuntimeInformation.ProcessArchitecture == Architecture.X64 || RuntimeInformation.ProcessArchitecture == Architecture.X86)
            ? "windivert" // Default to WinDivert only on Windows x64/x86.
            : "hosts";
    }

    public async Task<int> OnExecuteAsync(CommandLineApplication _, CancellationToken ct)
    {
        var webApplicationBuilder = WebApplication.CreateSlimBuilder();
        webApplicationBuilder.Configuration.AddUserSecrets<Program>();

        if (!NoTerminalUi)
        {
            webApplicationBuilder.Logging.AddKrpTerminalLogger();
            webApplicationBuilder.AddKrpTerminalUi();
        }
        else
        {
            webApplicationBuilder.Logging.AddKrpLogger();
        }

        var builder = webApplicationBuilder.Services.AddKubernetesForwarder(webApplicationBuilder.Configuration)
                .UseDnsLookup(options =>
                {
                    options.Nameserver = Nameserver;
                });

        switch (Routing)
        {
            case "hosts":
                builder.UseRouting(DnsOptions.HostsFile);
                break;
            case "windivert":
                builder.UseRouting(DnsOptions.WinDivert);
                break;
        }
        switch (Forwarder)
        {
            case "http":
                builder.UseHttpForwarder();
                break;
            case "tcp":
                builder.UseTcpForwarder(options =>
                {
                    options.ListenAddress = IPAddress.Any;
                    options.ListenPorts = [80, 443];
                });
                break;
            case "hybrid":
                builder.UseTcpWithHttpForwarder(options =>
                {
                    options.ListenAddress = IPAddress.Any;
                    options.ListenPorts = [80, 443];
                });

                // TODO: Should apply NoCertificateValidation for UseTcpForwarder switch case also
                builder.Services.PostConfigure<HttpForwarderOptions>(options =>
                {
                    options.SkipCertificateValidation = NoCertificateValidation;
                });

                break;
        }

        webApplicationBuilder.Services.Configure<ValidationOptions>(options =>
        {
            options.ExitOnFailure = NoTerminalUi;
        });

        if (!NoDiscovery)
        {
            builder.UseEndpointExplorer(options =>
            {
                options.RefreshInterval = TimeSpan.FromHours(1);
            });
        }

        var app = webApplicationBuilder.Build();
        app.UseKubernetesForwarder();

        using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct, app.Lifetime.ApplicationStopping);

        if (!NoTerminalUi)
        {
            var terminalUi = app.Services.GetRequiredService<KrpTerminalUi>();
            await Task.WhenAll(app.RunAsync(shutdownCts.Token), terminalUi.RunAsync(shutdownCts.Token));
        }
        else
        {
            await app.RunAsync(shutdownCts.Token);
        }

        return 0;
    }

    public static string GetVersion()
    {
        var infoVersion = VersionHelper.GetProductVersion();
        
        var parts = infoVersion.Split('+');
        var version = parts[0];
        var build = parts.Length > 1 ? parts[1] : null;

        return build is null
            ? $"krp version {version}"
            : $"krp version {version}, build {build}";
    }
}
