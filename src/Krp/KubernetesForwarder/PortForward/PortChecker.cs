using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Krp.KubernetesForwarder.PortForward;

public static class PortChecker
{
    private static readonly Lock _lock = new Lock();

    public static bool TryIsPortAvailable(int port)
    {
        // Opening the TCP listener to check if it its available is not thread-safe.
        lock (_lock)
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
}