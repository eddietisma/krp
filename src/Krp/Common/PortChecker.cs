using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Memory;

namespace Krp.Common;

public static class PortChecker
{
    private static readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private static readonly TimeSpan _ttl = TimeSpan.FromMilliseconds(500);

    public static bool TryIsPortAvailable(int port)
    {
        return _cache.GetOrCreate(port, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ttl;

            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            {
                try
                {
                    using var client = new TcpClient();
                    client.Connect("host.docker.internal", port);
                    return !client.Connected;
                }
                catch (SocketException)
                {
                    return true;
                }
            }

            var tcpConnInfoArray = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            return tcpConnInfoArray.All(x => x.Port != port);
        });
    }
}
