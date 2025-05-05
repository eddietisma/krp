using System.Net;

namespace Krp.KubernetesForwarder.TcpForwarder;

public class TcpForwarderOptions
{
    public IPAddress ListenAddress { get; set; }
    public int ListenPort { get; set; }
}