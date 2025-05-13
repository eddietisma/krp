using System;
using System.Net;
using System.Threading.Tasks;

namespace Krp.Endpoints;

public interface IEndpointHandler : IDisposable
{
    public string Host { get; set; }

    /// <summary>
    /// Used to determine if the port-forwarding was set in configuration or dynamically.
    /// </summary>
    public bool IsStatic { get; set; }
    public IPAddress LocalIp { get; set; }
    public int LocalPort { get; set; }
    public string Url { get; set; }
    public string Path { get; set; }

    public Task EnsureRunningAsync();
    public string GetDestinationUrl();
}