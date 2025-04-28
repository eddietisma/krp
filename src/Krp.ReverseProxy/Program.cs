using Krp.DependencyInjection;
using Krp.KubernetesForwarder.Models;
using Microsoft.AspNetCore.Hosting;
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
            .ConfigureServices(services =>
            {
                services.AddKubernetesForwarder()
                    .UseEndpoint(0, 80, "asgnmntattest", "assignment-attest-attestorder-grpcserver-api")
                    .UseEndpoint(0, 80, "asgnmntattest", "assignment-attest-notification-grpcserver-api")
                    .UseEndpoint(0, 80, "associdocs", "documents-api")
                    .UseEndpoint(0, 80, "handover", "handover-api")
                    .UseEndpoint(0, 80, "itc-actors", "actor-registry-api")
                    .UseEndpoint(0, 80, "itc-users", "user-api")
                    .UseEndpoint(0, 80, "sharedsvcs", "bisnodeintegration-grpc-api")
                    .UseEndpoint(0, 80, "sharedsvcs", "customer-api")
                    .UseEndpoint(0, 80, "sharedsvcs", "document-sign-grpc-api")
                    .UseEndpoint(0, 80, "sharedsvcs", "occupant-api")
                    .UseEndpoint(0, 80, "sharedsvcs", "person-api")
                    .UseEndpoint(0, 80, "sharedsvcs", "person-economy-grpc-api")
                    .UseEndpoint(0, 80, "sharedsvcs", "person-grpcserver-api")
                    .UseEndpoint(0, 80, "sharedsvcs", "texthandler-api")
                    .UseEndpoint(0, 80, "sharedsvcs", "pdfgenerator-api")
                    .UseEndpointExplorer(options =>
                    {
                        //options.Filter = ["namespace/meetings/*", "namespace/*/service/person*"];
                        options.RefreshInterval = TimeSpan.FromHours(1);
                    })
                    .UseRouting(KrpRouting.WindowsHostsFile)
                    .UseHttpForwarder()
                    .UseTcpForwarder(options =>
                    {
                        options.DefaultTimeout = TimeSpan.FromSeconds(30);
                        options.DefaultBufferSize = 8192;
                        options.ListenAddress = IPAddress.Any;
                        options.ListenPort = 80;
                        options.MaxConnections = 100;
                    });
            });

}