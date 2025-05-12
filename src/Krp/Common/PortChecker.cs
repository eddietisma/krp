using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Krp.Common;

public static class PortChecker
{
    public static bool TryIsPortAvailable(int port)
    {
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
    }
}