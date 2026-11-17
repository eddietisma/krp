using System.Net;

namespace Krp.Forwarders.TcpForwarder;

public class TcpForwarderOptions
{
    public required IPAddress ListenAddress { get; set; }
    public required int[] ListenPorts { get; set; }
    public int HttpPort { get; set; } = 80;
    public int HttpsPort { get; set; } = 443;
}