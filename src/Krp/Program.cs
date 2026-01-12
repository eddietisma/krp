using Krp.DependencyInjection;
using Krp.Dns;
using Krp.Forwarders.HttpForwarder;
using Krp.Logging;
using Microsoft.AspNetCore.Builder;
using System;
using System.Net;

namespace Krp;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure logging
        builder.Logging.AddKrpLogger();

        // Register the forwarder and its extensions
        builder.Services.AddKubernetesForwarder(builder.Configuration)
            //.UseHttpEndpoint(5000, "http", "api.domain.com", "/api")
            //.UseHttpEndpoint(5001, "http", "api.domain.com", "/api/v2")
            //.UseEndpoint(9032, 80, "namespace", "service/myapi") // Specific local port mappings
            //.UseEndpoint(0, 80, "namespace", "service/myapi") // Dynamic local port selection
            .UseEndpointExplorer(options =>
            {
                //options.Filter = [
                //    "namespace/meetings/*",
                //    "namespace/*/service/person*",
                //];
                options.RefreshInterval = TimeSpan.FromHours(1);
            })
            .UseDnsLookup(options =>
            {
                options.Nameserver = "8.8.8.8";
            })
            //.UseHttpForwarder()
            //.UseTcpForwarder(options =>
            // {
            //    options.ListenAddress = IPAddress.Any;
            //    options.ListenPorts = [80, 443];
            // })
            .UseTcpWithHttpForwarder(options =>
            {
                options.ListenAddress = IPAddress.Any;
                options.ListenPorts = [80, 443];
            })
            .UseRouting(DnsOptions.HostsFile);

        var app = builder.Build();

        app.UseKubernetesForwarder();
        app.Run();
    }
}