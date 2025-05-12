using Krp.KubernetesForwarder.Endpoints.Models;
using System.Collections.Generic;

namespace Krp.DependencyInjection;

public class KubernetesForwarderOptions
{
    public List<KubernetesEndpoint> Endpoints { get; set; } = new();
    public List<HttpEndpoint> HttpEndpoints { get; set; } = new();
}