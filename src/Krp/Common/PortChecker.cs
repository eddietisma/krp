using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace Krp.Common;

public static class PortChecker
{
    private static readonly Lock _lock = new Lock();

    public static bool TryIsPortAvailable(int port)
    {
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            var ipAddress = System.Net.Dns.GetHostAddresses("host.docker.internal")
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

            // Opening the TCP listener to check if it its available is not thread-safe.
            lock (_lock)
            {
                try
                {
                    var tcpListener = new TcpListener(ipAddress, port);
                    tcpListener.Start();
                    tcpListener.Stop();
                    return true;
                }
                catch (SocketException)
                {
                    return false;
                }
            }
        }

        var tcpConnInfoArray = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        return tcpConnInfoArray.All(x => x.Port != port);
    }
}