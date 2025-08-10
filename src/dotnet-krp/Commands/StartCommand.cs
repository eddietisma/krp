using Krp.DependencyInjection;
using Krp.Dns;
using Krp.Forwarders.HttpForwarder;
using Krp.Logging;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Tool.Commands;

[Command(Name = "", OptionsComparison = StringComparison.InvariantCultureIgnoreCase)]
public class StartCommand
{
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
        var builder = WebApplication.CreateBuilder();

        builder.Logging.AddKrpLogger();
        var kubernetesForwarderBuilder = builder.Services.AddKubernetesForwarder(builder.Configuration)
            .UseDnsLookup(options =>
            {
                options.Nameserver = Nameservers;
            });

        switch (Routing)
        {
            case "hosts":
                kubernetesForwarderBuilder.UseRouting(DnsOptions.HostsFile);
                break;
        }
        switch (Forwarder)
        {
            case "http":
                kubernetesForwarderBuilder.UseHttpForwarder();
                break;
            case "tcp":
                kubernetesForwarderBuilder.UseTcpForwarder(options =>
                {
                    options.ListenAddress = IPAddress.Any;
                    options.ListenPorts = [80, 443];

                });
                break;
            case "hybrid":
                kubernetesForwarderBuilder.UseTcpWithHttpForwarder(options =>
                {
                    options.ListenAddress = IPAddress.Any;
                    options.ListenPorts = [80, 443];
                });
                break;
        }

        if (!NoDiscovery)
        {
            kubernetesForwarderBuilder.UseEndpointExplorer(options =>
            {
                options.RefreshInterval = TimeSpan.FromHours(1);
            });
        }

        var app = builder.Build();

        app.UseKubernetesForwarder();
        await app.RunAsync(ct);
        return 0;
    }
}
