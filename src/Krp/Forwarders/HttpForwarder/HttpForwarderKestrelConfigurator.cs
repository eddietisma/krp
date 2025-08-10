using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using System;

namespace Krp.Forwarders.HttpForwarder;

public class HttpForwarderKestrelConfigurator : IConfigureOptions<KestrelServerOptions>
{
    private readonly IServiceProvider _provider;
    private readonly HttpForwarderOptions _options;

    public HttpForwarderKestrelConfigurator(IOptions<HttpForwarderOptions> forwarderOptions, IServiceProvider provider)
    {
        _provider = provider;
        _options = forwarderOptions.Value;
    }

    public void Configure(KestrelServerOptions options)
    {
        options.ApplicationServices = _provider;
        options.ListenAnyIP(_options.HttpPort, listen => listen.Protocols = HttpProtocols.Http1);
        options.ListenAnyIP(_options.Http2Port, listen => listen.Protocols = HttpProtocols.Http2);
        options.ListenAnyIP(_options.HttpsPort, listen => 
        {
            listen.UseHttps(); // Use default dev certs for HTTPS.
            listen.Protocols = HttpProtocols.Http1;
        });
    }
}
