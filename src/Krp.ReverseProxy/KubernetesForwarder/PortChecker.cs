using System.Net;
using System.Net.Sockets;

namespace Krp.KubernetesForwarder;

public static class PortChecker
{
    public static bool TryIsPortAvailable(int port)
    {
        try
        {
            var tcpListener = new TcpListener(IPAddress.Loopback, port);
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