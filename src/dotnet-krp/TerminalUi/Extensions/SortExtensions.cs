using Krp.Common;
using Krp.Endpoints.PortForward;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Krp.Tool.TerminalUi.Extensions;

public enum SortField { Url, Resource, Namespace, Ip, PortForward }

public static class SortExtensions
{
    public static List<PortForwardEndpointHandler> Sort(this IEnumerable<PortForwardEndpointHandler> list, SortField sortField, bool sortAsc)
    {
        var ordered = sortField switch
        {
            SortField.PortForward => sortAsc
                ? list.OrderBy(h => h.IsActive).ThenBy(h => h.Url, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(h => h.IsActive).ThenBy(h => h.Url, StringComparer.OrdinalIgnoreCase),
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
}