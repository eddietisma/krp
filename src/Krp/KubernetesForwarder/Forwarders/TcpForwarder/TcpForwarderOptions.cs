using System.Net;

namespace Krp.KubernetesForwarder.Forwarders.TcpForwarder;

public class TcpForwarderOptions
{
    public IPAddress ListenAddress { get; set; }
    public int ListenPort { get; set; }
}