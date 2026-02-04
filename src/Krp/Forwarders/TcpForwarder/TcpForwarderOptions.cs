using System.Net;

namespace Krp.Forwarders.TcpForwarder;

public class TcpForwarderOptions
{
    public required IPAddress ListenAddress { get; set; }
    public required int[] ListenPorts { get; set; }
}