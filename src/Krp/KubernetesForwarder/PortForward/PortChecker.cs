using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;

namespace Krp.KubernetesForwarder.PortForward;

public static class PortChecker
{
    private static readonly Lock _lock = new Lock();

    public static bool TryIsPortAvailable(int port)
    {
        var tcpConnInfoArray = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        return tcpConnInfoArray.All(x => x.Port != port);
    }
}