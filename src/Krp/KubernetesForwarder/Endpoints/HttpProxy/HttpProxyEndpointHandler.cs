using System;
using System.Net;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.Endpoints.HttpProxy;

public class HttpProxyEndpointHandler : IEndpointHandler
{
    public string Host { get; set; }
    public bool IsStatic { get; set; }
    public IPAddress LocalIp { get; set; }
    public int LocalPort { get; set; }
    public string Url { get; set; }
    public string Path { get; set; }

    public Task EnsureRunningAsync()
    {
        return Task.CompletedTask;
    }

    public string GetDestinationUrl()
    {
        return Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ?
            $"http://host.docker.internal:{LocalPort}" : 
            $"http://localhost:{LocalPort}";
    }

    public void Dispose()
    {
    }
}