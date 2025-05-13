using DnsClient;
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
    private readonly LookupClient _lookupClient;

    public DnsLookupHandler(IOptions<DnsLookupOptions> options)
    {
        _lookupClient = new LookupClient(IPAddress.Parse(options.Value.Nameserver));
    }

    public async Task<IPAddress> QueryAsync(string host)
    {
        var result = await _lookupClient.QueryAsync(new DnsQuestion(host, QueryType.A), new DnsQueryAndServerOptions
        {
            UseCache = true,
            Timeout = TimeSpan.FromSeconds(5),
        });

        var ip = result.Answers.ARecords().FirstOrDefault()?.Address;
        if (ip == null)
        {
            Console.WriteLine("No A record found.");
        }

        return ip;
    }
}