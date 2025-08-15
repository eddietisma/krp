using System.Net;

namespace Krp.Common;

public static class IpAddressHelper
{
    public static uint ToUInt32(this IPAddress ip)
    {
        if (Equals(ip, IPAddress.None))
        {
            return uint.MaxValue;
        }

        var bytes = ip.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3]; // bytes are in network-order (big-endian) → pack manually
    }
}