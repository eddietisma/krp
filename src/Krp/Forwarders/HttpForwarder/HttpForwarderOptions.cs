using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Krp.Forwarders.HttpForwarder;

public class HttpForwarderOptions
{
    public int Http2Port { get; set; } = 81;
    public int HttpPort { get; set; } = 80;
    public int HttpsPort { get; set; } = 443;
    public bool SkipCertificateValidation { get; set; } = true;

    // Kestrel updates these endpoints with the assigned ports when it binds the listeners.
    public ListenOptions? HttpListener { get; internal set; }
    public ListenOptions? Http2Listener { get; internal set; }
    public ListenOptions? HttpsListener { get; internal set; }
}