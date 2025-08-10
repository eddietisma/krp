using Krp.DependencyInjection;
using Krp.Dns;
using Krp.Forwarders.HttpForwarder;
using Krp.Logging;
using Krp.Tool.TerminalUi;
using Krp.Tool.TerminalUi.DependencyInjection;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Tool.Commands;

[Command(Name = "", OptionsComparison = StringComparison.InvariantCultureIgnoreCase)]
public class StartCommand
{
    [Option("--ui", Description = "Use Terminal UI")]
    public bool TerminalUi { get; init; } = true;

    [Option("--no-discovery|-nd", Description = "Disable automatic endpoint discovery")]
    public bool NoDiscovery { get; init; } = false;

    [Option("--nameservers|-n <NAMESERVERS>", Description = "Comma-separated list of DNS servers")]
    public string Nameservers { get; init; } = "8.8.8.8";

    [Option("--forwarder|-f <FORWARDER>", Description = "Connection method: tcp, http, or hybrid")]
    [AllowedValues("tcp", "http", "hybrid", IgnoreCase = true)]
    public string Forwarder { get; init; } = "hybrid";

    [Option("--routing|-r <ROUTING>", Description = "Routing method (currently only 'hosts')")]
    [AllowedValues("hosts", IgnoreCase = true)]
    public string Routing { get; init; } = "hosts";

    public async Task<int> OnExecuteAsync(CommandLineApplication _, CancellationToken ct)
    {
        WebApplicationBuilder webApplicationBuilder = WebApplication.CreateBuilder();

        if (TerminalUi)
        {
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

        var terminalUi = app.Services.GetRequiredService<KrpTerminalUi>();

        await Task.WhenAll(app.RunAsync(ct), terminalUi.RunUiAsync());

        return 0;
    }
}
