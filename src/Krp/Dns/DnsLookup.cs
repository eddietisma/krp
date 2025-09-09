using DnsClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Krp.Dns;

public interface IDnsLookupHandler
{
    Task<IPAddress> QueryAsync(string host);
}

public class DnsLookupHandler : IDnsLookupHandler
{
    private readonly ILogger<DnsLookupHandler> _logger;
    private readonly LookupClient _lookupClient;

    public DnsLookupHandler(IOptions<DnsLookupOptions> options, ILogger<DnsLookupHandler> logger)
    {
        _logger = logger;
        _lookupClient = new LookupClient(new LookupClientOptions(IPAddress.Parse(options.Value.Nameserver))
        {
            UseTcpOnly = true, // Avoid UDP/53 so our WinDivert UDP handler does not intercept our own lookups
            UseCache = true,
            Timeout = TimeSpan.FromSeconds(5),
        });
    }

    public async Task<IPAddress> QueryAsync(string host)
    {
        var result = await _lookupClient.QueryAsync(new DnsQuestion(host, QueryType.A));

        var ip = result.Answers.ARecords().FirstOrDefault()?.Address;
        if (ip == null)
        {
            _logger.LogError("No A record found for {host}", host);
        }

        return ip;
    }
}
