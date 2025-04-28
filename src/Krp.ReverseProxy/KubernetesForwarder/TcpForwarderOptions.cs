using System;
using System.Net;

namespace Krp.KubernetesForwarder;

public class TcpForwarderOptions
{
    public int DefaultBufferSize { get; set; }
    public TimeSpan DefaultTimeout { get; set; }
    public IPAddress ListenAddress { get; set; }
    public int ListenPort { get; set; }
    public int MaxConnections { get; set; }
}