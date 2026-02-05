using Krp.Endpoints.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Kubernetes;

public interface IKubernetesClient
{
    Task<string> FetchCurrentContext();
    Task<List<KubernetesEndpoint>> FetchServices(List<Regex> filters, CancellationToken ct);
    bool WaitForAccess(TimeSpan timeout, TimeSpan pollInterval);
}
