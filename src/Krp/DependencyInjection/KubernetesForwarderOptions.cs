using Krp.Endpoints.Models;
using System.Collections.Generic;

namespace Krp.DependencyInjection;

public class KubernetesForwarderOptions
{
    public List<KubernetesEndpoint> Endpoints { get; set; } = [];
    public List<HttpEndpoint> HttpEndpoints { get; set; } = [];
}