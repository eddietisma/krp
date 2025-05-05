using Krp.KubernetesForwarder.PortForward;
using Krp.KubernetesForwarder.Routing;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Tasks;

namespace Krp.KubernetesForwarder.HttpProxy;

public class HttpProxyHandler
{
    private readonly IDnsLookupHandler _dnsLookupHandler;
    private readonly ILogger<HttpProxyHandler> _logger;

    public int LocalPort { get; set; }
    public string Host { get; set; }

    public HttpProxyHandler(IDnsLookupHandler dnsLookupHandler, ILogger<HttpProxyHandler> logger)
    {
        _dnsLookupHandler = dnsLookupHandler;
        _logger = logger;
    }

    public async Task<string> GetDestinationUrl()
    {
        if (!PortChecker.TryIsPortAvailable(LocalPort))
        {
            _logger.LogError("Local port: '{port}' is available, routing to localhost", LocalPort);
            return $"http://localhost:{LocalPort}";
        }
        
        return await Task.FromResult(Host);
    }

    public async Task<IPAddress> GetRealIp()
    {
        var result = await _dnsLookupHandler.QueryAsync(Host);
        return result;
    }
}