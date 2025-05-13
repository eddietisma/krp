using System.Net;

namespace Krp.Forwarders.TcpForwarder;

public class TcpForwarderOptions
{
    public IPAddress ListenAddress { get; set; }
    public int[] ListenPorts { get; set; }
}