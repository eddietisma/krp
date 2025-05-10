using Krp.DependencyInjection;
using Krp.KubernetesForwarder.Dns;
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
                    .UseHttpEndpoint(5000, "gateway-api.qa.hsb.se", "/meetings")
                    .UseEndpoint(0, 80, "asgnmntattest", "assignment-attest-attestorder-grpcserver-api")
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
                    //    options.ListenPort = 80;

                    // })
                    .UseTcpWithHttpForwarder(options =>
                    {
                        options.ListenAddress = IPAddress.Any;
                        options.ListenPort = 80;
                    })
                    .UseRouting(DnsOptions.WindowsHostsFile);
            });

}