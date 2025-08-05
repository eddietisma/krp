using Krp.DependencyInjection;
using Krp.Dns;
using Microsoft.Extensions.Hosting;
using System;
using System.Net;

namespace Krp;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddKubernetesForwarder(context.Configuration)
                    //.UseHttpEndpoint(5000, "api.domain.com", "/api")
                    //.UseHttpEndpoint(5001, "api.domain.com", "/api/v2")
                    //.UseEndpoint(9032, 80, "namespace", "myapi") // Specific local port mappings
                    //.UseEndpoint(0, 80, "namespace", "myapi") // Dynamic local port selection
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
            });

}