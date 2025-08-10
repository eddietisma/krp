using Krp.Endpoints.PortForward;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Krp.Tool.TerminalUi.Extensions;

public enum SortField { Url, Resource, Namespace, Ip, PortForward }

public static class SortExtensions
{
    public static List<PortForwardEndpointHandler> Sort(this IEnumerable<PortForwardEndpointHandler> list, SortField sortField, bool sortAsc)
    {
        var ordered = sortField switch
        {
            SortField.PortForward => sortAsc
                ? list.OrderBy(h => h.IsActive)
                : list.OrderByDescending(h => h.IsActive),
            SortField.Resource => sortAsc
                ? list.OrderBy(h => h.Resource ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(h => h.Resource ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            SortField.Namespace => sortAsc
                ? list.OrderBy(h => h.Namespace ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(h => h.Namespace ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            SortField.Ip => sortAsc
                ? list.OrderBy(h => h.LocalIp.ToUInt32())
                : list.OrderByDescending(h => h.LocalIp.ToUInt32()),
            SortField.Url => sortAsc
                ? list.OrderBy(h => h.Url, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(h => h.Url, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(),
        };

        return ordered.ToList();
    }

    public static uint ToUInt32(this IPAddress ip)
    {
        if (Equals(ip, IPAddress.None))
        {
            return uint.MaxValue;
        }

        var bytes = ip.GetAddressBytes();
        return (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3]; // bytes are in network-order (big-endian) → pack manually
    }
}