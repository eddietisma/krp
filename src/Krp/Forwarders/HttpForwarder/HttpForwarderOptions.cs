namespace Krp.Forwarders.HttpForwarder;

public class HttpForwarderOptions
{
    public int Http2Port { get; set; } = 81;
    public int HttpPort { get; set; } = 80;
    public int HttpsPort { get; set; } = 443;
}