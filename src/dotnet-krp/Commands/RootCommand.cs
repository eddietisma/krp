using Krp.Common;
using Krp.DependencyInjection;
using Krp.Dns;
using Krp.Forwarders.HttpForwarder;
using Krp.Logging;
using Krp.Tool.TerminalUi;
using Krp.Tool.TerminalUi.DependencyInjection;
using Krp.Tool.TerminalUi.Logging;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Tool.Commands;

[Command(Name = "krp", OptionsComparison = StringComparison.InvariantCultureIgnoreCase)]
[VersionOptionFromMember("--version|-v", MemberName = nameof(GetVersion))]
public class RootCommand
{
    [Option("--no-ui", Description = "Disable terminal UI")]
    public bool NoTerminalUi { get; init; } = false;

    [Option("--no-discovery", Description = "Disable automatic Kubernetes endpoint discovery")]
    public bool NoDiscovery { get; init; } = false;

    [Option("--nameservers|-n <NAMESERVERS>", Description = "Comma-separated list of DNS servers, used for HTTP proxy endpoints")]
    public string Nameservers { get; init; } = "8.8.8.8";

    [Option("--forwarder|-f <FORWARDER>", Description = "Forwarding method")]
    [AllowedValues("tcp", "http", "hybrid", IgnoreCase = true)]
    public string Forwarder { get; init; } = "hybrid";

    [Option("--routing|-r <ROUTING>", Description = "Routing method")]
    [AllowedValues("hosts", "windivert", IgnoreCase = true)]
    public string Routing { get; init; } = "windivert";

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
                    options.Nameserver = Nameservers;
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
                break;
        }

        if (!NoDiscovery)
        {
            builder.UseEndpointExplorer(options =>
            {
                options.RefreshInterval = TimeSpan.FromHours(1);
            });
        }

        var app = webApplicationBuilder.Build();
        app.UseKubernetesForwarder();

        if (!NoTerminalUi)
        {
            var terminalUi = app.Services.GetRequiredService<KrpTerminalUi>();
            await Task.WhenAll(app.RunAsync(ct), terminalUi.RunUiAsync());
        }
        else
        {
            await app.RunAsync(ct);
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
