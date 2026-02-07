using Krp.Endpoints.Models;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Kubernetes;

public interface IKubernetesClient
{
    bool TryGetKubeConfigPath(out string path);
    Task<string> FetchCurrentContext();
    Task<List<KubernetesEndpoint>> FetchServices(List<Regex> filters, CancellationToken ct);
}
