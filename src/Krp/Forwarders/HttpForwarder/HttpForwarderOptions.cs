using System.IO;

namespace Krp.Forwarders.HttpForwarder;

public enum HttpForwarderInternalTransport
{
    Tcp,
    NamedPipe,
    UnixSocket,
}

public enum HttpForwarderInternalEndpoint
{
    Http1,
    Http2,
    Https,
}

public class HttpForwarderOptions
{
    public int Http2Port { get; set; } = 81;
    public int HttpPort { get; set; } = 80;
    public int HttpsPort { get; set; } = 443;
    public bool SkipCertificateValidation { get; set; } = true;

    public HttpForwarderInternalTransport InternalTransport { get; set; } = HttpForwarderInternalTransport.Tcp;
    public string PipeNamePrefix { get; set; } = "krp-http";
    public string UnixSocketDirectory { get; set; } = string.Empty;

    public string GetInternalPipeName(HttpForwarderInternalEndpoint endpoint)
    {
        return $"{PipeNamePrefix}-{GetEndpointSuffix(endpoint)}";
    }

    public string GetInternalUnixSocketPath(HttpForwarderInternalEndpoint endpoint)
    {
        var dir = string.IsNullOrWhiteSpace(UnixSocketDirectory) ? Path.GetTempPath() : UnixSocketDirectory;
        return Path.Combine(dir, $"{PipeNamePrefix}-{GetEndpointSuffix(endpoint)}.sock");
    }

    private static string GetEndpointSuffix(HttpForwarderInternalEndpoint endpoint)
    {
        return endpoint switch
        {
            HttpForwarderInternalEndpoint.Http1 => "h1",
            HttpForwarderInternalEndpoint.Http2 => "h2",
            HttpForwarderInternalEndpoint.Https => "https",
            _ => "h1",
        };
    }
}
